using CrediPrest.Domain.Enums;

namespace CrediPrest.Domain.Entities;

public sealed class EmailNotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public int NotificationVersion { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
}
