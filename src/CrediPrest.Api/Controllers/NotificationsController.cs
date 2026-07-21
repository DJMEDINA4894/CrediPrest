using System.Security.Claims;
using CrediPrest.Application.DTOs.Notifications;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class NotificationsController(
    INotificationService notificationService,
    IExpoPushNotificationService expoPushNotificationService,
    IWebPushNotificationService webPushNotificationService) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(CancellationToken cancellationToken)
    {
        await notificationService.RefreshAutomaticAsync(cancellationToken);
        return Ok(await notificationService.ListAsync(GetCurrentUserId(), GetCurrentClientId(), cancellationToken));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        await notificationService.MarkAsReadAsync(GetCurrentUserId(), GetCurrentClientId(), id, cancellationToken);
        return NoContent();
    }

    [HttpPost("push-devices")]
    public async Task<IActionResult> RegisterPushDevice(
        RegisterExpoPushDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await expoPushNotificationService.RegisterDeviceAsync(
            GetCurrentUserId(),
            GetCurrentClientId(),
            request.ExpoPushToken,
            request.Platform,
            request.DeviceName,
            cancellationToken);
        return NoContent();
    }

    [HttpPost("push-devices/unregister")]
    public async Task<IActionResult> UnregisterPushDevice(
        UnregisterExpoPushDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await expoPushNotificationService.UnregisterDeviceAsync(
            GetCurrentUserId(),
            GetCurrentClientId(),
            request.ExpoPushToken,
            cancellationToken);
        return NoContent();
    }

    [HttpGet("web-push/public-key")]
    public IActionResult GetWebPushPublicKey()
        => webPushNotificationService.IsConfigured
            ? Ok(new { publicKey = webPushNotificationService.PublicKey })
            : Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Web Push no está configurado en el servidor.",
                detail: "Configura las claves VAPID en Azure App Service.");

    [HttpPost("web-push/devices")]
    public async Task<IActionResult> RegisterWebPushDevice(
        RegisterWebPushDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await webPushNotificationService.RegisterDeviceAsync(
            GetCurrentUserId(),
            GetCurrentClientId(),
            request.Endpoint,
            request.Keys.P256dh,
            request.Keys.Auth,
            request.UserAgent,
            cancellationToken);
        return NoContent();
    }

    [HttpPost("web-push/devices/unregister")]
    public async Task<IActionResult> UnregisterWebPushDevice(
        UnregisterWebPushDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await webPushNotificationService.UnregisterDeviceAsync(
            GetCurrentUserId(),
            GetCurrentClientId(),
            request.Endpoint,
            cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Usuario no identificado.");

        return Guid.Parse(userId);
    }

    private Guid? GetCurrentClientId()
        => User.IsInRole("Client") && Guid.TryParse(User.FindFirstValue("clientId"), out var clientId)
            ? clientId
            : null;
}

public sealed record RegisterExpoPushDeviceRequest(string ExpoPushToken, string Platform, string? DeviceName);
public sealed record UnregisterExpoPushDeviceRequest(string ExpoPushToken);
public sealed record RegisterWebPushDeviceRequest(string Endpoint, WebPushKeysRequest Keys, string? UserAgent);
public sealed record WebPushKeysRequest(string P256dh, string Auth);
public sealed record UnregisterWebPushDeviceRequest(string Endpoint);
