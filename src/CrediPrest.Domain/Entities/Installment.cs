using CrediPrest.Domain.Enums;

namespace CrediPrest.Domain.Entities;

public sealed class Installment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public Loan Loan { get; set; } = null!;
    public int InstallmentNumber { get; set; }
    public DateTime DueDate { get; set; }
    public decimal PaymentAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;
    public DateTime? PaidAtUtc { get; set; }
    public decimal AmountPaid { get; set; }
    public List<Payment> Payments { get; set; } = [];
}
