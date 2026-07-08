namespace CrediPrest.Domain.Entities;

public sealed class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string IdentificationNumber { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PersonalReference1 { get; set; }
    public string? ReferencePhone1 { get; set; }
    public string? PersonalReference2 { get; set; }
    public string? ReferencePhone2 { get; set; }
    public string? BacAccountNumber { get; set; }
    public string? LafiseAccountNumber { get; set; }
    public string? BamproAccountNumber { get; set; }
    public string PreferredPaymentMethod { get; set; } = "cash";
    public bool HasKash { get; set; }
    public string? KashAccount { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public List<Loan> Loans { get; set; } = [];
}
