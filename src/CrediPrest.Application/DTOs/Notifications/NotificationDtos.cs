using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.DTOs.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    bool IsRead,
    DateTime CreatedAtUtc,
    Guid RelatedEntityId,
    Guid? RelatedLoanId,
    DateTime? DueDate);
