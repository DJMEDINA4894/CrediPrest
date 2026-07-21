using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrediPrest.Domain.Entities;
using CrediPrest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace CrediPrest.Api;

public interface IWebPushNotificationService
{
    string? PublicKey { get; }
    bool IsConfigured { get; }
    Task RegisterDeviceAsync(Guid userId, Guid? clientId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken cancellationToken);
    Task UnregisterDeviceAsync(Guid userId, Guid? clientId, string endpoint, CancellationToken cancellationToken);
    Task DispatchPendingAsync(CancellationToken cancellationToken);
}

internal sealed class WebPushNotificationService(
    AppDbContext dbContext,
    IConfiguration configuration,
    ILogger<WebPushNotificationService> logger) : IWebPushNotificationService
{
    private const int BatchSize = 100;
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);
    private static readonly WebPushClient PushClient = new();

    public string? PublicKey => Clean(configuration["WebPush:PublicKey"]);
    private string? PrivateKey => Clean(configuration["WebPush:PrivateKey"]);
    private string Subject => Clean(configuration["WebPush:Subject"]) ?? "mailto:denisjmedinac4894@gmail.com";
    public bool IsConfigured => PublicKey is not null && PrivateKey is not null;

    public async Task RegisterDeviceAsync(
        Guid userId,
        Guid? clientId,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        endpoint = endpoint.Trim();
        p256dh = p256dh.Trim();
        auth = auth.Trim();
        userAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("El endpoint de notificaciones del navegador no es válido.");
        }

        if (string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
        {
            throw new ArgumentException("La suscripción del navegador no contiene sus claves de cifrado.");
        }

        var endpointHash = HashEndpoint(endpoint);
        Guid? recipientUserId = clientId.HasValue ? null : userId;
        var device = await dbContext.WebPushDevices
            .FirstOrDefaultAsync(item => item.EndpointHash == endpointHash, cancellationToken);
        var now = DateTime.UtcNow;

        if (device is null)
        {
            dbContext.WebPushDevices.Add(new WebPushDevice
            {
                UserId = recipientUserId,
                ClientId = clientId,
                EndpointHash = endpointHash,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = userAgent,
                RegisteredAtUtc = now,
                LastSeenAtUtc = now
            });
        }
        else
        {
            var recipientChanged = device.UserId != recipientUserId || device.ClientId != clientId;
            if (recipientChanged || !device.IsActive)
            {
                device.RegisteredAtUtc = now;
            }

            device.UserId = recipientUserId;
            device.ClientId = clientId;
            device.Endpoint = endpoint;
            device.P256dh = p256dh;
            device.Auth = auth;
            device.UserAgent = userAgent;
            device.IsActive = true;
            device.LastSeenAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnregisterDeviceAsync(
        Guid userId,
        Guid? clientId,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var endpointHash = HashEndpoint(endpoint.Trim());
        var device = await dbContext.WebPushDevices.FirstOrDefaultAsync(
            item => item.EndpointHash == endpointHash
                && (clientId.HasValue ? item.ClientId == clientId : item.UserId == userId),
            cancellationToken);

        if (device is null)
        {
            return;
        }

        device.IsActive = false;
        device.LastSeenAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured || !await DispatchGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var devices = await dbContext.WebPushDevices
                .Where(device => device.IsActive)
                .ToListAsync(cancellationToken);
            if (devices.Count == 0)
            {
                return;
            }

            var pending = new List<PendingWebPush>();
            foreach (var device in devices)
            {
                var remaining = BatchSize - pending.Count;
                if (remaining == 0)
                {
                    break;
                }

                var notifications = await dbContext.Notifications
                    .Where(notification => device.ClientId.HasValue
                        ? notification.ClientId == device.ClientId
                        : notification.UserId == device.UserId)
                    .Where(notification => notification.CreatedAtUtc >= device.RegisteredAtUtc)
                    .Where(notification => !dbContext.WebPushDeliveries.Any(delivery =>
                        delivery.NotificationId == notification.Id
                        && delivery.WebPushDeviceId == device.Id
                        && delivery.NotificationVersion == notification.PushVersion))
                    .OrderBy(notification => notification.CreatedAtUtc)
                    .Take(remaining)
                    .ToListAsync(cancellationToken);

                pending.AddRange(notifications.Select(notification => new PendingWebPush(device, notification)));
            }

            if (pending.Count == 0)
            {
                return;
            }

            await ResolveRelatedLoansAsync(pending, cancellationToken);
            var vapidDetails = new VapidDetails(Subject, PublicKey!, PrivateKey!);
            foreach (var item in pending)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    title = item.Notification.Title,
                    body = "Tienes un aviso financiero pendiente. Abre CrediPrest para revisar los detalles.",
                    icon = "/crediprest-icon.png",
                    badge = "/crediprest-icon.png",
                    tag = $"crediprest-{item.Notification.Id}-{item.Notification.PushVersion}",
                    data = new
                    {
                        notificationId = item.Notification.Id,
                        relatedLoanId = item.RelatedLoanId,
                        url = item.RelatedLoanId.HasValue
                            ? $"/?pushNotificationId={item.Notification.Id}&pushLoanId={item.RelatedLoanId}"
                            : $"/?pushNotificationId={item.Notification.Id}&pushNotifications=1"
                    }
                });

                try
                {
                    var subscription = new PushSubscription(item.Device.Endpoint, item.Device.P256dh, item.Device.Auth);
                    await PushClient.SendNotificationAsync(subscription, payload, vapidDetails, cancellationToken);
                    dbContext.WebPushDeliveries.Add(CreateDelivery(item));
                }
                catch (WebPushException exception) when (IsExpiredSubscription(exception.StatusCode))
                {
                    item.Device.IsActive = false;
                    dbContext.WebPushDeliveries.Add(CreateDelivery(
                        item,
                        ((int)exception.StatusCode).ToString(),
                        "La suscripción del navegador expiró o fue eliminada."));
                }
                catch (WebPushException exception)
                {
                    logger.LogWarning(
                        exception,
                        "Web Push respondió con HTTP {StatusCode} para el dispositivo {DeviceId}; el aviso se reintentará.",
                        (int)exception.StatusCode,
                        item.Device.Id);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            DispatchGate.Release();
        }
    }

    private async Task ResolveRelatedLoansAsync(List<PendingWebPush> pending, CancellationToken cancellationToken)
    {
        var relatedIds = pending.Select(item => item.Notification.RelatedEntityId).Distinct().ToArray();
        var installmentLoans = await dbContext.Installments
            .Where(installment => relatedIds.Contains(installment.Id))
            .Select(installment => new { installment.Id, installment.LoanId })
            .ToDictionaryAsync(item => item.Id, item => item.LoanId, cancellationToken);
        var chargeLoans = await dbContext.LoanCharges
            .Where(charge => relatedIds.Contains(charge.Id))
            .Select(charge => new { charge.Id, charge.LoanId })
            .ToDictionaryAsync(item => item.Id, item => item.LoanId, cancellationToken);
        var paymentLoans = await dbContext.Payments
            .Where(payment => relatedIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.LoanId })
            .ToDictionaryAsync(item => item.Id, item => item.LoanId, cancellationToken);
        var loanIds = (await dbContext.Loans
            .Where(loan => relatedIds.Contains(loan.Id))
            .Select(loan => loan.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var item in pending)
        {
            var relatedId = item.Notification.RelatedEntityId;
            if (installmentLoans.TryGetValue(relatedId, out var installmentLoanId))
            {
                item.RelatedLoanId = installmentLoanId;
            }
            else if (chargeLoans.TryGetValue(relatedId, out var chargeLoanId))
            {
                item.RelatedLoanId = chargeLoanId;
            }
            else if (paymentLoans.TryGetValue(relatedId, out var paymentLoanId))
            {
                item.RelatedLoanId = paymentLoanId;
            }
            else if (loanIds.Contains(relatedId))
            {
                item.RelatedLoanId = relatedId;
            }
        }
    }

    private static WebPushDelivery CreateDelivery(
        PendingWebPush item,
        string? errorCode = null,
        string? errorMessage = null)
        => new()
        {
            NotificationId = item.Notification.Id,
            WebPushDeviceId = item.Device.Id,
            NotificationVersion = item.Notification.PushVersion,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    private static bool IsExpiredSubscription(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private static string HashEndpoint(string endpoint)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class PendingWebPush(WebPushDevice device, Notification notification)
    {
        public WebPushDevice Device { get; } = device;
        public Notification Notification { get; } = notification;
        public Guid? RelatedLoanId { get; set; }
    }
}
