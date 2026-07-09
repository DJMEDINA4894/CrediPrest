using CrediPrest.Domain.Enums;

namespace CrediPrest.Domain.Entities;

public sealed class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string? ReferenceName { get; set; }
    public decimal PrincipalAmount { get; set; }
    public CurrencyType Currency { get; set; }
    public decimal MonthlyInterestRate { get; set; }
    public int TermMonths { get; set; }
    public PaymentFrequency PaymentFrequency { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Active;
    public string? Notes { get; set; }
    public decimal TotalInterest { get; set; }
    public decimal TotalToPay { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Installment> Installments { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}
