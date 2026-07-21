using System.Net;
using System.Net.Mail;
using Azure;
using Azure.Communication.Email;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using CrediPrest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Api;

public interface IEmailNotificationService
{
    bool IsConfigured { get; }
    Task DispatchPendingAsync(CancellationToken cancellationToken);
}

internal sealed class AzureEmailNotificationService(
    AppDbContext dbContext,
    IConfiguration configuration,
    ILogger<AzureEmailNotificationService> logger) : IEmailNotificationService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);

    private bool Enabled => configuration.GetValue<bool>("Email:Enabled");
    private string? ConnectionString => Clean(configuration["Email:ConnectionString"]);
    private string? SenderAddress => Clean(configuration["Email:SenderAddress"]);
    private string SenderName => Clean(configuration["Email:SenderName"]) ?? "CrediPrest";
    private string? ApplicationUrl => Clean(configuration["Email:ApplicationUrl"])?.TrimEnd('/');

    public bool IsConfigured => Enabled
        && ConnectionString is not null
        && SenderAddress is not null
        && ApplicationUrl is not null;

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured || !await DispatchGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var state = await dbContext.EmailDispatchStates.SingleOrDefaultAsync(cancellationToken);
            if (state is null)
            {
                dbContext.EmailDispatchStates.Add(new EmailDispatchState { ActivatedAtUtc = DateTime.UtcNow });
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Canal de correo activado. Los avisos anteriores no se enviarán retroactivamente.");
                return;
            }

            var now = DateTime.UtcNow;
            var notifications = await dbContext.Notifications
                .AsNoTracking()
                .Include(notification => notification.User)
                .Include(notification => notification.Client)
                .Where(notification => notification.CreatedAtUtc >= state.ActivatedAtUtc)
                .Where(notification => notification.UserId.HasValue
                    ? notification.User != null && notification.User.IsActive && notification.User.Email != ""
                    : notification.Client != null && notification.Client.IsActive && notification.Client.Email != null)
                .Where(notification => !dbContext.EmailNotificationDeliveries.Any(delivery =>
                    delivery.NotificationId == notification.Id
                    && delivery.NotificationVersion == notification.PushVersion
                    && (delivery.Status == EmailDeliveryStatus.Submitted
                        || delivery.AttemptCount >= MaxAttempts
                        || delivery.NextAttemptAtUtc > now)))
                .OrderBy(notification => notification.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (notifications.Count == 0)
            {
                return;
            }

            var relatedLoans = await ResolveRelatedLoansAsync(notifications, cancellationToken);
            var emailClient = new EmailClient(ConnectionString!);
            foreach (var notification in notifications)
            {
                var recipientEmail = notification.UserId.HasValue
                    ? notification.User?.Email
                    : notification.Client?.Email;
                var recipientName = notification.UserId.HasValue
                    ? notification.User?.FullName
                    : notification.Client?.FullName;
                recipientEmail = NormalizeEmail(recipientEmail);
                recipientName = Clean(recipientName) ?? "Cliente";

                if (!IsValidEmail(recipientEmail))
                {
                    await RecordInvalidRecipientAsync(notification, recipientEmail, recipientName, cancellationToken);
                    continue;
                }

                var delivery = await GetOrCreateDeliveryAsync(
                    notification,
                    recipientEmail!,
                    recipientName,
                    cancellationToken);
                delivery.AttemptCount++;
                delivery.Status = EmailDeliveryStatus.Pending;
                delivery.LastAttemptAtUtc = now;
                delivery.NextAttemptAtUtc = now.AddMinutes(15);
                delivery.ErrorCode = null;
                delivery.ErrorMessage = null;
                await dbContext.SaveChangesAsync(cancellationToken);

                var relatedLoanId = relatedLoans.TryGetValue(notification.RelatedEntityId, out var loanId)
                    ? loanId
                    : (Guid?)null;
                var actionUrl = BuildActionUrl(notification.Id, relatedLoanId);
                var content = BuildContent(notification, recipientName, actionUrl);

                try
                {
                    var message = new EmailMessage(SenderAddress!, recipientEmail!, content);
                    var operation = await emailClient.SendAsync(WaitUntil.Started, message, cancellationToken);
                    delivery.Status = EmailDeliveryStatus.Submitted;
                    delivery.ProviderMessageId = operation.Id;
                    delivery.SubmittedAtUtc = DateTime.UtcNow;
                    delivery.NextAttemptAtUtc = null;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (RequestFailedException exception)
                {
                    MarkFailed(delivery, exception.ErrorCode, exception.Message);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogWarning(
                        exception,
                        "Azure Email rechazó el aviso {NotificationId} para {RecipientEmail}; se reintentará.",
                        notification.Id,
                        recipientEmail);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    MarkFailed(delivery, exception.GetType().Name, exception.Message);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogWarning(
                        exception,
                        "No se pudo enviar el aviso {NotificationId} por correo; se reintentará.",
                        notification.Id);
                }
            }
        }
        finally
        {
            DispatchGate.Release();
        }
    }

    private async Task<EmailNotificationDelivery> GetOrCreateDeliveryAsync(
        Notification notification,
        string recipientEmail,
        string recipientName,
        CancellationToken cancellationToken)
    {
        var delivery = await dbContext.EmailNotificationDeliveries.FirstOrDefaultAsync(item =>
            item.NotificationId == notification.Id
            && item.NotificationVersion == notification.PushVersion
            && item.RecipientEmail == recipientEmail,
            cancellationToken);

        if (delivery is not null)
        {
            delivery.RecipientName = recipientName;
            return delivery;
        }

        delivery = new EmailNotificationDelivery
        {
            NotificationId = notification.Id,
            NotificationVersion = notification.PushVersion,
            RecipientEmail = recipientEmail,
            RecipientName = recipientName
        };
        dbContext.EmailNotificationDeliveries.Add(delivery);
        return delivery;
    }

    private async Task RecordInvalidRecipientAsync(
        Notification notification,
        string? recipientEmail,
        string recipientName,
        CancellationToken cancellationToken)
    {
        var emailSnapshot = string.IsNullOrWhiteSpace(recipientEmail) ? "sin-correo" : recipientEmail;
        var delivery = await GetOrCreateDeliveryAsync(
            notification,
            emailSnapshot,
            recipientName,
            cancellationToken);
        delivery.Status = EmailDeliveryStatus.Failed;
        delivery.AttemptCount = MaxAttempts;
        delivery.LastAttemptAtUtc = DateTime.UtcNow;
        delivery.ErrorCode = "InvalidRecipient";
        delivery.ErrorMessage = "El destinatario no tiene un correo válido.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, Guid>> ResolveRelatedLoansAsync(
        IReadOnlyCollection<Notification> notifications,
        CancellationToken cancellationToken)
    {
        var relatedIds = notifications.Select(item => item.RelatedEntityId).Distinct().ToArray();
        var result = await dbContext.Installments
            .Where(installment => relatedIds.Contains(installment.Id))
            .Select(installment => new { installment.Id, installment.LoanId })
            .ToDictionaryAsync(item => item.Id, item => item.LoanId, cancellationToken);
        var chargeLoans = await dbContext.LoanCharges
            .Where(charge => relatedIds.Contains(charge.Id))
            .Select(charge => new { charge.Id, charge.LoanId })
            .ToListAsync(cancellationToken);
        foreach (var charge in chargeLoans)
        {
            result[charge.Id] = charge.LoanId;
        }

        var loanIds = await dbContext.Loans
            .Where(loan => relatedIds.Contains(loan.Id))
            .Select(loan => loan.Id)
            .ToListAsync(cancellationToken);
        foreach (var loanId in loanIds)
        {
            result[loanId] = loanId;
        }

        return result;
    }

    private EmailContent BuildContent(Notification notification, string recipientName, string actionUrl)
    {
        var safeBrand = WebUtility.HtmlEncode(SenderName);
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeTitle = WebUtility.HtmlEncode(notification.Title);
        var safeMessage = WebUtility.HtmlEncode(notification.Message);
        var safeUrl = WebUtility.HtmlEncode(actionUrl);
        var subject = $"{SenderName}: {notification.Title}";
        var plainText = $"Hola {recipientName},\n\n{notification.Message}\n\nRevisa el aviso en CrediPrest: {actionUrl}\n\nEste es un correo automático.";
        var html = $$"""
            <!doctype html>
            <html lang="es">
            <body style="margin:0;background:#edf3f5;font-family:Arial,sans-serif;color:#172333">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#edf3f5;padding:28px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;background:#ffffff;border:1px solid #d8e2e7;border-radius:8px;overflow:hidden">
                    <tr><td style="background:#116b62;padding:22px 28px;color:#ffffff">
                      <div style="font-size:13px;font-weight:700;text-transform:uppercase">Aviso financiero</div>
                      <div style="font-size:26px;font-weight:700;margin-top:4px">{{safeBrand}}</div>
                    </td></tr>
                    <tr><td style="padding:28px">
                      <p style="margin:0 0 18px;font-size:16px">Hola <strong>{{safeName}}</strong>,</p>
                      <h1 style="margin:0 0 12px;font-size:22px;color:#172333">{{safeTitle}}</h1>
                      <p style="margin:0 0 24px;font-size:16px;line-height:1.55;color:#405467">{{safeMessage}}</p>
                      <a href="{{safeUrl}}" style="display:inline-block;background:#116b62;color:#ffffff;text-decoration:none;font-weight:700;padding:13px 20px;border-radius:6px">Ver en CrediPrest</a>
                    </td></tr>
                    <tr><td style="border-top:1px solid #e3eaee;padding:18px 28px;font-size:12px;line-height:1.5;color:#687b89">
                      Este mensaje fue generado automáticamente. No respondas a este correo ni compartas tus credenciales.
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        return new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html
        };
    }

    private string BuildActionUrl(Guid notificationId, Guid? loanId)
        => loanId.HasValue
            ? $"{ApplicationUrl}/?pushNotificationId={notificationId}&pushLoanId={loanId}"
            : $"{ApplicationUrl}/?pushNotificationId={notificationId}&pushNotifications=1";

    private static void MarkFailed(EmailNotificationDelivery delivery, string? errorCode, string errorMessage)
    {
        delivery.Status = EmailDeliveryStatus.Failed;
        delivery.ErrorCode = Clean(errorCode);
        delivery.ErrorMessage = errorMessage.Length <= 600 ? errorMessage : errorMessage[..600];
        delivery.NextAttemptAtUtc = delivery.AttemptCount >= MaxAttempts
            ? null
            : DateTime.UtcNow.Add(GetRetryDelay(delivery.AttemptCount));
    }

    private static TimeSpan GetRetryDelay(int attempt)
        => attempt switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1)
        };

    private static bool IsValidEmail(string? email)
        => email is not null
            && MailAddress.TryCreate(email, out var parsed)
            && string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeEmail(string? email)
        => Clean(email)?.ToLowerInvariant();

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
