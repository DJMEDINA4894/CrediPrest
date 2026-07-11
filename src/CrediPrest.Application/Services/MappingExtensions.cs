using CrediPrest.Application.DTOs.Clients;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.Services;

internal static class MappingExtensions
{
    public static ClientDto ToDto(this Client client)
    {
        var activeLoans = client.Loans.Count(loan => loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue);
        var pendingCordobas = client.Loans
            .Where(loan => loan.Currency == CurrencyType.Cordoba)
            .Sum(loan => loan.TotalToPay - GetAppliedInstallmentAmount(loan) + GetPendingChargesAmount(loan));
        var pendingUsd = client.Loans
            .Where(loan => loan.Currency == CurrencyType.Usd)
            .Sum(loan => loan.TotalToPay - GetAppliedInstallmentAmount(loan) + GetPendingChargesAmount(loan));

        return new ClientDto(
            client.Id,
            client.FullName,
            client.IdentificationNumber,
            client.Phone,
            client.Address,
            client.Email,
            client.PersonalReference1,
            client.ReferencePhone1,
            client.PersonalReference2,
            client.ReferencePhone2,
            client.BacAccountNumber,
            client.LafiseAccountNumber,
            client.BamproAccountNumber,
            string.IsNullOrWhiteSpace(client.PreferredPaymentMethod) ? "cash" : client.PreferredPaymentMethod,
            client.HasKash,
            client.KashAccount,
            client.Notes,
            client.IsActive,
            client.RegisteredAtUtc,
            activeLoans,
            Math.Max(0, pendingCordobas),
            Math.Max(0, pendingUsd));
    }

    public static InstallmentDto ToDto(this Installment installment)
        => new(
            installment.Id,
            installment.InstallmentNumber,
            installment.DueDate,
            installment.PrincipalAmount,
            installment.InterestAmount,
            installment.PaymentAmount,
            installment.RemainingBalance,
            installment.Status,
            installment.PaidAtUtc,
            installment.AmountPaid);

    public static LoanChargeDto ToDto(this LoanCharge charge)
        => new(
            charge.Id,
            (int)charge.Type,
            charge.PeriodNumber,
            charge.PeriodStartDate,
            charge.PeriodEndDate,
            charge.Amount,
            charge.AmountPaid,
            Math.Max(0, charge.Amount - charge.AmountPaid),
            charge.Notes);

    public static LoanDto ToDto(this Loan loan)
    {
        var installmentPaid = GetAppliedInstallmentAmount(loan);
        var lateFeesTotal = loan.Charges.Sum(charge => charge.Amount);
        var lateFeesPaid = loan.Charges.Sum(charge => charge.AmountPaid);
        var lateFeesPending = GetPendingChargesAmount(loan);
        var totalPaid = installmentPaid + lateFeesPaid;

        return new LoanDto(
            loan.Id,
            loan.ClientId,
            loan.Client.FullName,
            loan.Client.IdentificationNumber,
            loan.LenderUser?.FullName,
            loan.LenderUser?.IdentificationNumber,
            loan.ReferenceName,
            loan.PrincipalAmount,
            loan.Currency,
            loan.MonthlyInterestRate,
            loan.TermMonths,
            loan.PaymentFrequency,
            loan.StartDate,
            loan.EndDate,
            loan.Status,
            loan.TotalInterest,
            loan.TotalToPay,
            totalPaid,
            lateFeesTotal,
            lateFeesPaid,
            lateFeesPending,
            Math.Max(0, loan.TotalToPay - installmentPaid) + lateFeesPending,
            loan.Notes,
            loan.AgreementCity,
            loan.LateFeeDescription);
    }

    private static decimal GetAppliedInstallmentAmount(Loan loan)
        => Math.Min(loan.TotalToPay, loan.Installments.Sum(installment => installment.AmountPaid));

    private static decimal GetPendingChargesAmount(Loan loan)
        => loan.Charges.Sum(charge => Math.Max(0, charge.Amount - charge.AmountPaid));

    public static LoanDetailDto ToDetailDto(this Loan loan)
        => new(
            loan.ToDto(),
            loan.Installments
                .OrderBy(installment => installment.InstallmentNumber)
                .Select(installment => installment.ToDto())
                .ToList(),
            loan.Charges
                .OrderBy(charge => charge.PeriodNumber)
                .Select(charge => charge.ToDto())
                .ToList());

    public static PaymentDto ToDto(this Payment payment)
        => new(
            payment.Id,
            payment.LoanId,
            payment.InstallmentId,
            payment.LoanChargeId,
            payment.PaymentDate,
            payment.AmountPaid,
            payment.PaymentMethod,
            payment.ReferenceNumber,
            payment.Notes,
            payment.ReceiptId,
            payment.Receipt?.FileName);
}
