using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.DTOs.Loans;

public sealed record InstallmentDto(
    Guid Id,
    int InstallmentNumber,
    DateTime DueDate,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal PaymentAmount,
    decimal RemainingBalance,
    InstallmentStatus Status,
    DateTime? PaidAtUtc,
    decimal AmountPaid);

public sealed record LoanDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string? ReferenceName,
    decimal PrincipalAmount,
    CurrencyType Currency,
    decimal MonthlyInterestRate,
    int TermMonths,
    PaymentFrequency PaymentFrequency,
    DateTime StartDate,
    DateTime EndDate,
    LoanStatus Status,
    decimal TotalInterest,
    decimal TotalToPay,
    decimal TotalPaid,
    decimal PendingBalance,
    string? Notes);

public sealed record LoanDetailDto(
    LoanDto Loan,
    IReadOnlyList<InstallmentDto> Installments);

public sealed record CreateLoanRequest(
    Guid ClientId,
    decimal PrincipalAmount,
    CurrencyType Currency,
    decimal MonthlyInterestRate,
    int TermMonths,
    PaymentFrequency PaymentFrequency,
    DateTime StartDate,
    string? ReferenceName,
    string? Notes);

public sealed record UpdateLoanRequest(
    decimal PrincipalAmount,
    CurrencyType Currency,
    decimal MonthlyInterestRate,
    int TermMonths,
    PaymentFrequency PaymentFrequency,
    DateTime StartDate,
    LoanStatus Status,
    string? ReferenceName,
    string? Notes);
