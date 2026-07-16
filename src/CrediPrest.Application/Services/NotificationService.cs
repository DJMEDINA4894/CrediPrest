using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Notifications;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace CrediPrest.Application.Services;

internal sealed class NotificationService(
    IApplicationDbContext dbContext,
    ILoanService loanService,
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly SemaphoreSlim PaymentNotificationGate = new(1, 1);

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, Guid? clientId, CancellationToken cancellationToken = default)
    {
        var notifications = await dbContext.Notifications
            .Where(notification => clientId.HasValue
                ? notification.ClientId == clientId
                : notification.UserId == userId)
            .ToListAsync(cancellationToken);

        var relatedEntityIds = notifications
            .Select(notification => notification.RelatedEntityId)
            .Distinct()
            .ToArray();
        var installments = await dbContext.Installments
            .Where(installment => relatedEntityIds.Contains(installment.Id))
            .ToDictionaryAsync(installment => installment.Id, cancellationToken);
        var charges = await dbContext.LoanCharges
            .Where(charge => relatedEntityIds.Contains(charge.Id))
            .ToDictionaryAsync(charge => charge.Id, cancellationToken);
        var relatedLoanIds = (await dbContext.Loans
            .Where(loan => relatedEntityIds.Contains(loan.Id))
            .Select(loan => loan.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return notifications
            .OrderBy(notification => GetRelatedDate(notification, installments, charges, relatedLoanIds))
            .ThenBy(notification => notification.IsRead)
            .ThenByDescending(notification => notification.CreatedAtUtc)
            .Take(50)
            .Select(notification =>
            {
                installments.TryGetValue(notification.RelatedEntityId, out var installment);
                charges.TryGetValue(notification.RelatedEntityId, out var charge);
                return new NotificationDto(
                    notification.Id,
                    notification.Type,
                    notification.Title,
                    notification.Message,
                    notification.IsRead,
                    notification.CreatedAtUtc,
                    notification.RelatedEntityId,
                    installment?.LoanId ?? charge?.LoanId ?? (relatedLoanIds.Contains(notification.RelatedEntityId) ? notification.RelatedEntityId : null),
                    installment?.DueDate ?? charge?.PeriodEndDate);
            })
            .ToList();
    }

    public async Task RefreshAutomaticAsync(CancellationToken cancellationToken = default)
    {
        await loanService.RefreshOverdueAsync(cancellationToken);
        await EnsurePaymentNotificationsAsync(cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid userId, Guid? clientId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(item => item.Id == notificationId
                && (clientId.HasValue ? item.ClientId == clientId : item.UserId == userId), cancellationToken)
            ?? throw new KeyNotFoundException("Notificación no encontrada.");

        notification.IsRead = true;
        notification.ReadAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsurePaymentNotificationsAsync(CancellationToken cancellationToken)
    {
        await PaymentNotificationGate.WaitAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        try
        {
            if (dbContext is DbContext databaseContext)
            {
                transaction = await databaseContext.Database.BeginTransactionAsync(cancellationToken);
                await databaseContext.Database.ExecuteSqlRawAsync(
                    "EXEC sp_getapplock @Resource = N'CrediPrest.PaymentNotifications', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;",
                    cancellationToken);
            }

            var today = BusinessClock.Today;
            var users = await dbContext.Users
                .Where(user => user.IsActive)
                .ToListAsync(cancellationToken);
            var staffUsers = users
                .Where(user => user.Role is UserRole.Admin or UserRole.Lender)
                .ToList();

            var paymentNotifications = await dbContext.Notifications
                .Where(notification => notification.Type == NotificationType.OverdueInstallment
                    || notification.Type == NotificationType.DueTodayInstallment
                    || notification.Type == NotificationType.LateFeeWarning
                    || notification.Type == NotificationType.LateFeeApplied)
                .ToListAsync(cancellationToken);
            var installmentNotificationIds = paymentNotifications
                .Where(notification => notification.Type is NotificationType.OverdueInstallment
                    or NotificationType.DueTodayInstallment
                    or NotificationType.LateFeeWarning)
                .Select(notification => notification.RelatedEntityId)
                .Distinct()
                .ToArray();
            var notificationInstallments = await dbContext.Installments
                .Include(installment => installment.Loan)
                .ThenInclude(loan => loan.Client)
                .Where(installment => installmentNotificationIds.Contains(installment.Id))
                .ToDictionaryAsync(installment => installment.Id, cancellationToken);
            var chargeNotificationIds = paymentNotifications
                .Where(notification => notification.Type == NotificationType.LateFeeApplied)
                .Select(notification => notification.RelatedEntityId)
                .Distinct()
                .ToArray();
            var notificationCharges = await dbContext.LoanCharges
                .Include(charge => charge.Loan)
                .ThenInclude(loan => loan.Client)
                .Where(charge => chargeNotificationIds.Contains(charge.Id))
                .ToDictionaryAsync(charge => charge.Id, cancellationToken);

            var obsoleteNotifications = paymentNotifications.Where(notification => notification.Type switch
            {
                NotificationType.OverdueInstallment or NotificationType.DueTodayInstallment =>
                    !notificationInstallments.TryGetValue(notification.RelatedEntityId, out var installment)
                    || !installment.Loan.Client.IsActive
                    || installment.Loan.Status == LoanStatus.Cancelled
                    || installment.AmountPaid >= installment.PaymentAmount
                    || installment.DueDate.Date > today,
                NotificationType.LateFeeWarning =>
                    !notificationInstallments.TryGetValue(notification.RelatedEntityId, out var warningInstallment)
                    || !warningInstallment.Loan.Client.IsActive
                    || warningInstallment.Loan.Status == LoanStatus.Cancelled,
                NotificationType.LateFeeApplied =>
                    !notificationCharges.TryGetValue(notification.RelatedEntityId, out var charge)
                    || !charge.Loan.Client.IsActive
                    || charge.Loan.Status == LoanStatus.Cancelled
                    || charge.AmountPaid >= charge.Amount,
                _ => false
            }).ToList();

            if (obsoleteNotifications.Count > 0)
            {
                dbContext.Notifications.RemoveRange(obsoleteNotifications);
                paymentNotifications.RemoveAll(obsoleteNotifications.Contains);
            }

            var installments = await dbContext.Installments
                .Include(installment => installment.Loan)
                .ThenInclude(loan => loan.Client)
                .Where(installment => installment.Status != InstallmentStatus.Paid
                    && installment.Loan.Client.IsActive
                    && installment.DueDate.Date <= today)
                .ToListAsync(cancellationToken);

            foreach (var installment in installments)
            {
                var isOverdue = installment.DueDate.Date < today;
                var type = isOverdue ? NotificationType.OverdueInstallment : NotificationType.DueTodayInstallment;
                var pendingAmount = Math.Max(0, installment.PaymentAmount - installment.AmountPaid);
                var dueDate = FormatDate(installment.DueDate);
                var title = isOverdue ? "Pago atrasado" : "Pago vence hoy";
                var staffMessage = $"{installment.Loan.Client.FullName} tiene la cuota {installment.InstallmentNumber} {(isOverdue ? "atrasada" : "pendiente para hoy")}, con vencimiento {dueDate}, por {pendingAmount:N2}.";
                var clientMessage = $"Tu cuota {installment.InstallmentNumber} {(isOverdue ? "está atrasada" : "vence hoy")}, con vencimiento {dueDate}, por {pendingAmount:N2}.";

                foreach (var user in staffUsers.Where(user => user.Role == UserRole.Admin || installment.Loan.LenderUserId == user.Id))
                {
                    await AddNotificationIfMissingAsync(user.Id, type, installment.Id, title, staffMessage, cancellationToken);
                }

                await AddClientNotificationIfMissingAsync(
                    installment.Loan.ClientId,
                    type,
                    installment.Id,
                    title,
                    clientMessage,
                    cancellationToken);

                foreach (var notification in paymentNotifications.Where(notification => notification.RelatedEntityId == installment.Id && notification.Type != type))
                {
                    dbContext.Notifications.Remove(notification);
                }
            }

            var loans = await dbContext.Loans
                .Include(loan => loan.Client)
                .Include(loan => loan.Installments)
                .Include(loan => loan.Payments)
                .Include(loan => loan.Charges)
                .Where(loan => loan.Client.IsActive
                    && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue))
                .ToListAsync(cancellationToken);

            foreach (var loan in loans)
            {
                foreach (var period in LateFeeCalculator.BuildPeriods(loan))
                {
                    var periodInstallmentIds = period.Installments.Select(item => item.Id).ToHashSet();
                    var warningNotifications = paymentNotifications.Where(notification =>
                        notification.Type == NotificationType.LateFeeWarning
                        && periodInstallmentIds.Contains(notification.RelatedEntityId)).ToList();
                    var charge = loan.Charges.FirstOrDefault(item =>
                        item.Type == LoanChargeType.LateFee && item.PeriodNumber == period.Number);
                    var appliedNotifications = charge is null
                        ? []
                        : paymentNotifications.Where(notification =>
                            notification.Type == NotificationType.LateFeeApplied
                            && notification.RelatedEntityId == charge.Id).ToList();

                    if (charge is not null)
                    {
                        foreach (var notification in warningNotifications)
                        {
                            dbContext.Notifications.Remove(notification);
                        }

                        foreach (var notification in appliedNotifications)
                        {
                            if (charge.AmountPaid >= charge.Amount)
                            {
                                dbContext.Notifications.Remove(notification);
                            }
                        }

                        if (charge.AmountPaid < charge.Amount)
                        {
                            var title = "Mora aplicada";
                            var amount = FormatMoney(charge.Amount - charge.AmountPaid, loan.Currency);
                            var month = FormatMonth(period.StartDate);
                            var staffMessage = $"{loan.Client.FullName} tiene mora aplicada al período de {month} (período {period.Number}) por {amount}.";
                            var clientMessage = $"Se aplicó una mora de {amount} al período de {month} de tu préstamo.";
                            await AddLateFeeNotificationsAsync(loan, NotificationType.LateFeeApplied, charge.Id, title, staffMessage, clientMessage, users, cancellationToken);
                        }
                    }

                    var nextInstallmentAtRisk = period.Installments
                        .Where(item => item.AmountPaid < item.PaymentAmount
                            && item.DueDate.Date >= today
                            && item.DueDate.Date <= today.AddDays(7))
                        .OrderBy(item => item.DueDate)
                        .FirstOrDefault();
                    foreach (var notification in warningNotifications.Where(notification =>
                        nextInstallmentAtRisk is null || notification.RelatedEntityId != nextInstallmentAtRisk.Id))
                    {
                        dbContext.Notifications.Remove(notification);
                    }

                    if (nextInstallmentAtRisk is null)
                    {
                        continue;
                    }

                    var estimatedLateFee = LateFeeCalculator.Calculate(
                        loan,
                        [nextInstallmentAtRisk],
                        nextInstallmentAtRisk.DueDate.Date.AddDays(1)).Amount;
                    if (estimatedLateFee <= 0)
                    {
                        continue;
                    }

                    var warningTitle = "Mora próxima";
                    var warningAmount = FormatMoney(estimatedLateFee, loan.Currency);
                    var dueDate = nextInstallmentAtRisk.DueDate.Date;
                    var warningStaffMessage = $"{loan.Client.FullName} tiene pendiente la cuota {nextInstallmentAtRisk.InstallmentNumber}. Si no la completa a más tardar el {dueDate:dd/MM/yyyy}, desde el día siguiente se aplicará una mora estimada de {warningAmount}.";
                    var warningClientMessage = $"Tu cuota {nextInstallmentAtRisk.InstallmentNumber} sigue pendiente. Si no la completas a más tardar el {dueDate:dd/MM/yyyy}, desde el día siguiente se aplicará una mora estimada de {warningAmount}.";
                    await AddLateFeeNotificationsAsync(loan, NotificationType.LateFeeWarning, nextInstallmentAtRisk.Id, warningTitle, warningStaffMessage, warningClientMessage, users, cancellationToken);
                }
            }

            if (dbContext is DbContext trackedContext && !trackedContext.ChangeTracker.HasChanges())
            {
                return;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                exception,
                "No se pudieron sincronizar las notificaciones automáticas por una actualización concurrente; se continuará con la consulta.");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            PaymentNotificationGate.Release();
        }
    }

    private async Task AddNotificationIfMissingAsync(
        Guid userId,
        NotificationType type,
        Guid relatedEntityId,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Notifications.FirstOrDefaultAsync(
            notification => notification.UserId == userId
                && notification.Type == type
                && notification.RelatedEntityId == relatedEntityId,
            cancellationToken);

        if (existing is not null)
        {
            var contentChanged = existing.Title != title || existing.Message != message;
            existing.Title = title;
            existing.Message = message;
            if (contentChanged)
            {
                existing.IsRead = false;
                existing.ReadAtUtc = null;
                existing.CreatedAtUtc = DateTime.UtcNow;
            }
            return;
        }

        dbContext.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = type,
            RelatedEntityId = relatedEntityId,
            Title = title,
            Message = message
        });
    }

    private async Task AddLateFeeNotificationsAsync(
        Loan loan,
        NotificationType type,
        Guid relatedEntityId,
        string title,
        string staffMessage,
        string clientMessage,
        IReadOnlyList<User> users,
        CancellationToken cancellationToken)
    {
        foreach (var user in users.Where(user => user.Role is UserRole.Admin or UserRole.Lender
            && (user.Role == UserRole.Admin || loan.LenderUserId == user.Id)))
        {
            await AddNotificationIfMissingAsync(user.Id, type, relatedEntityId, title, staffMessage, cancellationToken);
        }

        await AddClientNotificationIfMissingAsync(
            loan.ClientId,
            type,
            relatedEntityId,
            title,
            clientMessage,
            cancellationToken);
    }

    private async Task AddClientNotificationIfMissingAsync(
        Guid clientId,
        NotificationType type,
        Guid relatedEntityId,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Notifications.FirstOrDefaultAsync(
            notification => notification.ClientId == clientId
                && notification.Type == type
                && notification.RelatedEntityId == relatedEntityId,
            cancellationToken);

        if (existing is not null)
        {
            var contentChanged = existing.Title != title || existing.Message != message;
            existing.Title = title;
            existing.Message = message;
            if (contentChanged)
            {
                existing.IsRead = false;
                existing.ReadAtUtc = null;
                existing.CreatedAtUtc = DateTime.UtcNow;
            }
            return;
        }

        dbContext.Notifications.Add(new Notification
        {
            ClientId = clientId,
            Type = type,
            RelatedEntityId = relatedEntityId,
            Title = title,
            Message = message
        });
    }

    private static DateTime GetRelatedDate(
        Notification notification,
        IReadOnlyDictionary<Guid, Installment> installments,
        IReadOnlyDictionary<Guid, LoanCharge> charges,
        IReadOnlySet<Guid> loanIds)
        => installments.TryGetValue(notification.RelatedEntityId, out var installment)
            ? installment.DueDate
            : charges.TryGetValue(notification.RelatedEntityId, out var charge)
                ? charge.PeriodEndDate
                : loanIds.Contains(notification.RelatedEntityId)
                    ? notification.CreatedAtUtc
                : DateTime.MaxValue;

    private static string FormatMoney(decimal amount, CurrencyType currency)
        => $"{(currency == CurrencyType.Usd ? "USD" : "C$")} {amount:N2}";

    private static string FormatMonth(DateTime date)
        => date.ToString("MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-NI"));

    private static string FormatDate(DateTime date)
        => date.ToString("d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-NI"));
}
