using CrediPrest.Domain.Enums;

namespace CrediPrest.Domain.Entities;

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public Loan Loan { get; set; } = null!;
    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }
    public Guid? LoanChargeId { get; set; }
    public LoanCharge? LoanCharge { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal AmountPaid { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? ReceiptId { get; set; }
    public PaymentReceipt? Receipt { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
