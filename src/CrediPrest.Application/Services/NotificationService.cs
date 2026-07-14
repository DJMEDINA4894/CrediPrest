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

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var notifications = await dbContext.Notifications
            .Where(notification => notification.UserId == userId)
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

        return notifications
            .OrderBy(notification => GetRelatedDate(notification, installments, charges))
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
                    installment?.LoanId ?? charge?.LoanId,
                    installment?.DueDate ?? charge?.PeriodEndDate);
            })
            .ToList();
    }

    public async Task RefreshAutomaticAsync(CancellationToken cancellationToken = default)
    {
        await loanService.RefreshOverdueAsync(cancellationToken);
        await EnsurePaymentNotificationsAsync(cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId, cancellationToken)
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

            var today = DateTime.UtcNow.Date;
            var users = await dbContext.Users
                .Where(user => user.IsActive)
                .ToListAsync(cancellationToken);
            var staffUsers = users
                .Where(user => user.Role is UserRole.Admin or UserRole.Lender)
                .ToList();
            var clientUsers = users
                .Where(user => user.Role == UserRole.Client && user.ClientId.HasValue)
                .ToList();

            var paymentNotifications = await dbContext.Notifications
                .Where(notification => notification.Type == NotificationType.OverdueInstallment
                    || notification.Type == NotificationType.DueTodayInstallment
                    || notification.Type == NotificationType.LateFeeWarning
                    || notification.Type == NotificationType.LateFeeApplied)
                .ToListAsync(cancellationToken);
            var paidInstallmentIds = await dbContext.Installments
                .Where(installment => installment.AmountPaid >= installment.PaymentAmount)
                .Select(installment => installment.Id)
                .ToListAsync(cancellationToken);

            foreach (var notification in paymentNotifications.Where(notification => paidInstallmentIds.Contains(notification.RelatedEntityId)))
            {
                dbContext.Notifications.Remove(notification);
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
                var title = isOverdue ? "Pago atrasado" : "Pago vence hoy";
                var staffMessage = $"{installment.Loan.Client.FullName} tiene la cuota {installment.InstallmentNumber} {(isOverdue ? "atrasada" : "pendiente para hoy")} por {pendingAmount:N2}.";
                var clientMessage = $"Tu cuota {installment.InstallmentNumber} {(isOverdue ? "está atrasada" : "vence hoy")} por {pendingAmount:N2}.";

                foreach (var user in staffUsers.Where(user => user.Role == UserRole.Admin || installment.Loan.LenderUserId == user.Id))
                {
                    await AddNotificationIfMissingAsync(user.Id, type, installment.Id, title, staffMessage, cancellationToken);
                }

                foreach (var user in clientUsers.Where(user => user.ClientId == installment.Loan.ClientId))
                {
                    await AddNotificationIfMissingAsync(user.Id, type, installment.Id, title, clientMessage, cancellationToken);
                }

                foreach (var notification in paymentNotifications.Where(notification => notification.RelatedEntityId == installment.Id && notification.Type != type))
                {
                    dbContext.Notifications.Remove(notification);
                }
            }

            var loans = await dbContext.Loans
                .Include(loan => loan.Client)
                .Include(loan => loan.Installments)
                .Include(loan => loan.Charges)
                .Where(loan => loan.Client.IsActive
                    && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue))
                .ToListAsync(cancellationToken);

            foreach (var loan in loans)
            {
                foreach (var period in LateFeeCalculator.BuildPeriods(loan))
                {
                    var firstInstallment = period.Installments[0];
                    var warningNotifications = paymentNotifications.Where(notification =>
                        notification.Type == NotificationType.LateFeeWarning
                        && notification.RelatedEntityId == firstInstallment.Id).ToList();
                    var charge = loan.Charges.FirstOrDefault(item =>
                        item.Type == LoanChargeType.LateFee && item.PeriodNumber == period.Number);
                    var appliedNotifications = charge is null
                        ? []
                        : paymentNotifications.Where(notification =>
                            notification.Type == NotificationType.LateFeeApplied
                            && notification.RelatedEntityId == charge.Id).ToList();

                    if (charge is not null)
                    {
                        foreach (var notification in warningNotifications.Concat(appliedNotifications))
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

                        continue;
                    }

                    var hasPendingAmount = period.Installments.Any(item => item.AmountPaid < item.PaymentAmount);
                    if (!hasPendingAmount || today < period.EndDate.AddDays(-7) || today >= period.EndDate)
                    {
                        foreach (var notification in warningNotifications)
                        {
                            dbContext.Notifications.Remove(notification);
                        }

                        continue;
                    }

                    var estimatedLateFee = LateFeeCalculator.Calculate(loan, period.Installments, period.EndDate).Amount;
                    if (estimatedLateFee <= 0)
                    {
                        continue;
                    }

                    var warningTitle = "Mora próxima";
                    var warningAmount = FormatMoney(estimatedLateFee, loan.Currency);
                    var warningMonth = FormatMonth(period.StartDate);
                    var warningStaffMessage = $"{loan.Client.FullName} tiene pendiente el período de {warningMonth}. Si no regulariza antes del {period.EndDate:dd/MM/yyyy}, se aplicará una mora estimada de {warningAmount}.";
                    var warningClientMessage = $"Tu período de {warningMonth} sigue pendiente. Si no regularizas antes del {period.EndDate:dd/MM/yyyy}, se aplicará una mora estimada de {warningAmount}.";
                    await AddLateFeeNotificationsAsync(loan, NotificationType.LateFeeWarning, firstInstallment.Id, warningTitle, warningStaffMessage, warningClientMessage, users, cancellationToken);
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
            existing.Title = title;
            existing.Message = message;
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

        foreach (var user in users.Where(user => user.Role == UserRole.Client && user.ClientId == loan.ClientId))
        {
            await AddNotificationIfMissingAsync(user.Id, type, relatedEntityId, title, clientMessage, cancellationToken);
        }
    }

    private static DateTime GetRelatedDate(
        Notification notification,
        IReadOnlyDictionary<Guid, Installment> installments,
        IReadOnlyDictionary<Guid, LoanCharge> charges)
        => installments.TryGetValue(notification.RelatedEntityId, out var installment)
            ? installment.DueDate
            : charges.TryGetValue(notification.RelatedEntityId, out var charge)
                ? charge.PeriodEndDate
                : DateTime.MaxValue;

    private static string FormatMoney(decimal amount, CurrencyType currency)
        => $"{(currency == CurrencyType.Usd ? "USD" : "C$")} {amount:N2}";

    private static string FormatMonth(DateTime date)
        => date.ToString("MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-NI"));
}
