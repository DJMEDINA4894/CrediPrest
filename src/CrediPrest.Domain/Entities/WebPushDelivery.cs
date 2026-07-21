namespace CrediPrest.Domain.Entities;

public sealed class WebPushDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public Guid WebPushDeviceId { get; set; }
    public int NotificationVersion { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
}
