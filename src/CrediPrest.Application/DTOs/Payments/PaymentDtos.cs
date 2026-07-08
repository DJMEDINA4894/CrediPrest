using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.DTOs.Payments;

public sealed record PaymentDto(
    Guid Id,
    Guid LoanId,
    Guid InstallmentId,
    DateTime PaymentDate,
    decimal AmountPaid,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes);

public sealed record RegisterPaymentRequest(
    Guid LoanId,
    Guid? InstallmentId,
    DateTime PaymentDate,
    decimal AmountPaid,
    PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Notes);
