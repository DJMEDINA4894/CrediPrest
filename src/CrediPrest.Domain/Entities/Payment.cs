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
    public PaymentType Type { get; set; } = PaymentType.Regular;
    public PaymentMethod PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? ReceiptId { get; set; }
    public PaymentReceipt? Receipt { get; set; }
    public LoanRecalculationMode? RecalculationMode { get; set; }
    public decimal? PreviousOutstandingPrincipal { get; set; }
    public decimal? NewOutstandingPrincipal { get; set; }
    public decimal? PreviousInstallmentAmount { get; set; }
    public decimal? NewInstallmentAmount { get; set; }
    public int? PreviousInstallmentCount { get; set; }
    public int? NewInstallmentCount { get; set; }
    public decimal? PreviousPendingInterest { get; set; }
    public decimal? NewPendingInterest { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
