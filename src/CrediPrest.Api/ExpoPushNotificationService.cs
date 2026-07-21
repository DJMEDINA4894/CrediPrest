using System.Net.Http.Json;
using System.Text.Json;
using CrediPrest.Domain.Entities;
using CrediPrest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Api;

public interface IExpoPushNotificationService
{
    Task RegisterDeviceAsync(Guid userId, Guid? clientId, string token, string platform, string? deviceName, CancellationToken cancellationToken);
    Task UnregisterDeviceAsync(Guid userId, Guid? clientId, string token, CancellationToken cancellationToken);
    Task DispatchPendingAsync(CancellationToken cancellationToken);
}

internal sealed class ExpoPushNotificationService(
    AppDbContext dbContext,
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ExpoPushNotificationService> logger) : IExpoPushNotificationService
{
    private const int BatchSize = 100;
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RegisterDeviceAsync(
        Guid userId,
        Guid? clientId,
        string token,
        string platform,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        token = token.Trim();
        platform = platform.Trim().ToLowerInvariant();
        deviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();

        if (!IsExpoPushToken(token))
        {
            throw new ArgumentException("El token de notificaciones de Expo no es válido.");
        }

        if (platform is not ("android" or "ios"))
        {
            throw new ArgumentException("La plataforma del dispositivo no es válida.");
        }

        Guid? recipientUserId = clientId.HasValue ? null : userId;
        var device = await dbContext.ExpoPushDevices
            .FirstOrDefaultAsync(item => item.ExpoPushToken == token, cancellationToken);
        var now = DateTime.UtcNow;

        if (device is null)
        {
            dbContext.ExpoPushDevices.Add(new ExpoPushDevice
            {
                UserId = recipientUserId,
                ClientId = clientId,
                ExpoPushToken = token,
                Platform = platform,
                DeviceName = deviceName,
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
            device.Platform = platform;
            device.DeviceName = deviceName;
            device.IsActive = true;
            device.LastSeenAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnregisterDeviceAsync(
        Guid userId,
        Guid? clientId,
        string token,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.ExpoPushDevices.FirstOrDefaultAsync(
            item => item.ExpoPushToken == token
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
        if (!await DispatchGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var devices = await dbContext.ExpoPushDevices
                .Where(device => device.IsActive)
                .ToListAsync(cancellationToken);
            if (devices.Count == 0)
            {
                return;
            }

            var pending = new List<PendingPush>();
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
                    .Where(notification => !dbContext.ExpoPushDeliveries.Any(delivery =>
                        delivery.NotificationId == notification.Id
                        && delivery.ExpoPushDeviceId == device.Id
                        && delivery.NotificationVersion == notification.PushVersion))
                    .OrderBy(notification => notification.CreatedAtUtc)
                    .Take(remaining)
                    .ToListAsync(cancellationToken);

                pending.AddRange(notifications.Select(notification => new PendingPush(device, notification)));
            }

            if (pending.Count == 0)
            {
                return;
            }

            await ResolveRelatedLoansAsync(pending, cancellationToken);
            var messages = pending.Select(item => new ExpoPushMessage(
                item.Device.ExpoPushToken,
                "default",
                item.Notification.Title,
                item.Notification.Message,
                "high",
                "crediprest-alerts",
                new ExpoPushData(
                    item.Notification.Id.ToString(),
                    item.RelatedLoanId?.ToString(),
                    item.RelatedLoanId.HasValue ? "payments" : "notifications"))).ToArray();

            using var request = new HttpRequestMessage(HttpMethod.Post, "--/api/v2/push/send")
            {
                Content = JsonContent.Create(messages, options: JsonOptions)
            };
            request.Headers.Accept.ParseAdd("application/json");
            var accessToken = configuration["ExpoPush:AccessToken"];
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Expo Push respondió con HTTP {StatusCode}; los avisos se reintentarán.", (int)response.StatusCode);
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(JsonOptions, cancellationToken);
            if (result?.Data is null || result.Data.Count != pending.Count)
            {
                logger.LogWarning("Expo Push devolvió una cantidad inesperada de tickets; los avisos se reintentarán.");
                return;
            }

            for (var index = 0; index < pending.Count; index++)
            {
                var item = pending[index];
                var ticket = result.Data[index];
                dbContext.ExpoPushDeliveries.Add(new ExpoPushDelivery
                {
                    NotificationId = item.Notification.Id,
                    ExpoPushDeviceId = item.Device.Id,
                    NotificationVersion = item.Notification.PushVersion,
                    ExpoTicketId = ticket.Id,
                    ErrorCode = ticket.Details?.Error,
                    ErrorMessage = ticket.Message
                });

                if (string.Equals(ticket.Details?.Error, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase))
                {
                    item.Device.IsActive = false;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "No se pudo conectar con Expo Push; los avisos se reintentarán.");
        }
        finally
        {
            DispatchGate.Release();
        }
    }

    private async Task ResolveRelatedLoansAsync(List<PendingPush> pending, CancellationToken cancellationToken)
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

    private static bool IsExpoPushToken(string token)
        => (token.StartsWith("ExpoPushToken[", StringComparison.Ordinal)
            || token.StartsWith("ExponentPushToken[", StringComparison.Ordinal))
            && token.EndsWith(']');

    private sealed class PendingPush(ExpoPushDevice device, Notification notification)
    {
        public ExpoPushDevice Device { get; } = device;
        public Notification Notification { get; } = notification;
        public Guid? RelatedLoanId { get; set; }
    }

    private sealed record ExpoPushMessage(
        string To,
        string Sound,
        string Title,
        string Body,
        string Priority,
        string ChannelId,
        ExpoPushData Data);

    private sealed record ExpoPushData(string NotificationId, string? RelatedLoanId, string Target);

    private sealed class ExpoPushResponse
    {
        public List<ExpoPushTicket> Data { get; set; } = [];
    }

    private sealed class ExpoPushTicket
    {
        public string Status { get; set; } = string.Empty;
        public string? Id { get; set; }
        public string? Message { get; set; }
        public ExpoPushTicketDetails? Details { get; set; }
    }

    private sealed class ExpoPushTicketDetails
    {
        public string? Error { get; set; }
    }
}
