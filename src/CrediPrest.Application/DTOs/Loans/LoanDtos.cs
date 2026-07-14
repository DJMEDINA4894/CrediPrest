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

public sealed record LoanChargeDto(
    Guid Id,
    int Type,
    int PeriodNumber,
    DateTime PeriodStartDate,
    DateTime PeriodEndDate,
    decimal Amount,
    decimal AmountPaid,
    decimal PendingAmount,
    string? Notes,
    DateTime CalculatedAtUtc,
    IReadOnlyList<LoanChargeAllocationDto> Allocations);

public sealed record LoanChargeAllocationDto(
    Guid InstallmentId,
    decimal Amount,
    decimal AmountPaid,
    decimal PendingAmount);

public sealed record LoanDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string ClientIdentificationNumber,
    string? LenderName,
    string? LenderIdentificationNumber,
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
    decimal LateFeesTotal,
    decimal LateFeesPaid,
    decimal LateFeesPending,
    decimal PendingBalance,
    string? Notes,
    string? AgreementCity,
    string? LateFeeDescription);

public sealed record LoanDetailDto(
    LoanDto Loan,
    IReadOnlyList<InstallmentDto> Installments,
    IReadOnlyList<LoanChargeDto> Charges);

public sealed record CreateLoanRequest(
    Guid ClientId,
    decimal PrincipalAmount,
    CurrencyType Currency,
    decimal MonthlyInterestRate,
    int TermMonths,
    PaymentFrequency PaymentFrequency,
    DateTime StartDate,
    string? ReferenceName,
    string? Notes,
    string? AgreementCity,
    string? LateFeeDescription);

public sealed record UpdateLoanRequest(
    decimal PrincipalAmount,
    CurrencyType Currency,
    decimal MonthlyInterestRate,
    int TermMonths,
    PaymentFrequency PaymentFrequency,
    DateTime StartDate,
    LoanStatus Status,
    string? ReferenceName,
    string? Notes,
    string? AgreementCity,
    string? LateFeeDescription);

public sealed record ExtraordinaryPaymentPreviewRequest(
    LoanRecalculationMode Mode,
    DateTime EffectiveDate,
    decimal Amount,
    int? NewInstallmentCount);

public sealed record RegisterExtraordinaryPaymentRequest(
    LoanRecalculationMode Mode,
    DateTime EffectiveDate,
    decimal Amount,
    int? NewInstallmentCount,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes);

public sealed record LoanRecalculationPreviewDto(
    Guid LoanId,
    LoanRecalculationMode Mode,
    DateTime EffectiveDate,
    DateTime FirstDueDate,
    decimal OutstandingPrincipal,
    decimal ExtraordinaryPaymentAmount,
    decimal PrincipalAfterPayment,
    decimal CurrentInstallmentAmount,
    decimal NewInstallmentAmount,
    int PaidInstallments,
    int CurrentRemainingInstallments,
    int NewRemainingInstallments,
    decimal CurrentPendingInterest,
    decimal NewInterest,
    decimal InterestSavings,
    decimal NewPendingTotal);
