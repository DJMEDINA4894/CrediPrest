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
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly SemaphoreSlim PaymentNotificationGate = new(1, 1);

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await EnsurePaymentNotificationsAsync(cancellationToken);

        return await dbContext.Notifications
            .Where(notification => notification.UserId == userId)
            .OrderBy(notification => notification.IsRead)
            .ThenByDescending(notification => notification.CreatedAtUtc)
            .Take(50)
            .Select(notification => new NotificationDto(
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.IsRead,
                notification.CreatedAtUtc,
                notification.RelatedEntityId))
            .ToListAsync(cancellationToken);
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

            if (staffUsers.Count == 0 && clientUsers.Count == 0)
            {
                return;
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
        var exists = await dbContext.Notifications.AnyAsync(
            notification => notification.UserId == userId
                && notification.Type == type
                && notification.RelatedEntityId == relatedEntityId,
            cancellationToken);

        if (exists)
        {
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
}
