namespace CrediPrest.Domain.Entities;

public sealed class PaymentReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public byte[] Content { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
