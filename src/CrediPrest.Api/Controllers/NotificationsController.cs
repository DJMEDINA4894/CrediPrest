using System.Security.Claims;
using CrediPrest.Application.DTOs.Notifications;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(CancellationToken cancellationToken)
        => Ok(await notificationService.ListAsync(GetCurrentUserId(), cancellationToken));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        await notificationService.MarkAsReadAsync(GetCurrentUserId(), id, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Usuario no identificado.");

        return Guid.Parse(userId);
    }
}
