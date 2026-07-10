using CrediPrest.Domain.Enums;

namespace CrediPrest.Domain.Entities;

public sealed class LoanCharge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public Loan Loan { get; set; } = null!;
    public LoanChargeType Type { get; set; } = LoanChargeType.LateFee;
    public int PeriodNumber { get; set; }
    public DateTime PeriodStartDate { get; set; }
    public DateTime PeriodEndDate { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public string? Notes { get; set; }
    public List<Payment> Payments { get; set; } = [];
}
