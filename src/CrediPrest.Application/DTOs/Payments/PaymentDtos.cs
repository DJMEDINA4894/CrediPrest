using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.DTOs.Payments;

public sealed record PaymentDto(
    Guid Id,
    Guid LoanId,
    Guid? InstallmentId,
    Guid? LoanChargeId,
    DateTime PaymentDate,
    decimal AmountPaid,
    PaymentType Type,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes,
    Guid? ReceiptId,
    string? ReceiptFileName,
    LoanRecalculationMode? RecalculationMode,
    decimal? PreviousOutstandingPrincipal,
    decimal? NewOutstandingPrincipal,
    decimal? PreviousInstallmentAmount,
    decimal? NewInstallmentAmount,
    int? PreviousInstallmentCount,
    int? NewInstallmentCount,
    decimal? PreviousPendingInterest,
    decimal? NewPendingInterest);

public sealed record RegisterPaymentRequest(
    Guid LoanId,
    Guid? InstallmentId,
    DateTime PaymentDate,
    decimal AmountPaid,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes,
    string? ReceiptImageBase64,
    string? ReceiptFileName,
    string? ReceiptContentType);

public sealed record PaymentReceiptFileDto(string FileName, string ContentType, byte[] Content);
