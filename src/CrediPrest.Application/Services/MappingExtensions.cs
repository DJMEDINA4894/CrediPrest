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
        var cordobasBreakdown = SumPaidBreakdown(client.Loans.Where(loan => loan.Currency == CurrencyType.Cordoba));
        var usdBreakdown = SumPaidBreakdown(client.Loans.Where(loan => loan.Currency == CurrencyType.Usd));

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
            Math.Max(0, pendingUsd),
            cordobasBreakdown.Principal,
            cordobasBreakdown.Interest,
            usdBreakdown.Principal,
            usdBreakdown.Interest);
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
            GetEffectiveInstallmentStatus(installment),
            installment.PaidAtUtc,
            installment.AmountPaid);

    public static LoanChargeDto ToDto(this LoanCharge charge, Loan loan)
    {
        var period = LateFeeCalculator.BuildPeriods(loan)
            .FirstOrDefault(item => item.Number == charge.PeriodNumber);
        var calculatedAllocations = period is null
            ? []
            : LateFeeCalculator.Calculate(loan, period.Installments, charge.AppliedAtUtc.Date).Allocations;
        var calculatedTotal = calculatedAllocations.Sum(allocation => allocation.Amount);
        var allocationAmounts = new List<(LateFeeInstallmentAllocation Allocation, decimal Amount)>();
        var allocatedTotal = 0m;

        for (var index = 0; index < calculatedAllocations.Count; index++)
        {
            var allocation = calculatedAllocations[index];
            var isLast = index == calculatedAllocations.Count - 1;
            var amount = isLast
                ? charge.Amount - allocatedTotal
                : calculatedTotal > 0
                    ? Math.Round(charge.Amount * allocation.Amount / calculatedTotal, 2)
                    : 0m;
            amount = Math.Max(0, amount);
            allocatedTotal += amount;
            allocationAmounts.Add((allocation, amount));
        }

        var remainingPaid = charge.AmountPaid;
        var allocations = allocationAmounts.Select(item =>
        {
            var amountPaid = Math.Round(Math.Min(item.Amount, Math.Max(0, remainingPaid)), 2);
            remainingPaid = Math.Round(Math.Max(0, remainingPaid - amountPaid), 2);
            return new LoanChargeAllocationDto(
                item.Allocation.InstallmentId,
                item.Amount,
                amountPaid,
                Math.Max(0, item.Amount - amountPaid));
        }).ToList();

        return new LoanChargeDto(
            charge.Id,
            (int)charge.Type,
            charge.PeriodNumber,
            charge.PeriodStartDate,
            charge.PeriodEndDate,
            charge.Amount,
            charge.AmountPaid,
            Math.Max(0, charge.Amount - charge.AmountPaid),
            charge.Notes,
            charge.AppliedAtUtc,
            allocations);
    }

    public static LoanDto ToDto(this Loan loan)
    {
        var installmentPaid = GetAppliedInstallmentAmount(loan);
        var lateFeesTotal = loan.Charges.Sum(charge => charge.Amount);
        var lateFeesPaid = loan.Charges.Sum(charge => charge.AmountPaid);
        var lateFeesPending = GetPendingChargesAmount(loan);
        var extraordinaryPrincipalPaid = loan.Payments
            .Where(payment => payment.Type == PaymentType.ExtraordinaryPrincipal)
            .Sum(payment => payment.AmountPaid);
        var paidBreakdown = GetPaidBreakdown(loan);
        var totalPaid = installmentPaid + lateFeesPaid + extraordinaryPrincipalPaid;

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
            paidBreakdown.Principal,
            paidBreakdown.Interest,
            lateFeesTotal,
            lateFeesPaid,
            lateFeesPending,
            Math.Max(0, loan.TotalToPay - installmentPaid - extraordinaryPrincipalPaid) + lateFeesPending,
            loan.Notes,
            loan.AgreementCity,
            string.IsNullOrWhiteSpace(loan.LateFeeDescription) ? "50%" : loan.LateFeeDescription);
    }

    private static decimal GetAppliedInstallmentAmount(Loan loan)
        => Math.Min(loan.TotalToPay, loan.Installments.Sum(installment => installment.AmountPaid));

    private static PaidBreakdown GetPaidBreakdown(Loan loan)
    {
        var paidInterest = loan.Installments.Sum(installment =>
            Math.Min(Math.Max(0, installment.AmountPaid), installment.InterestAmount));
        var paidPrincipalFromInstallments = loan.Installments.Sum(installment =>
        {
            var appliedAmount = Math.Max(0, installment.AmountPaid);
            var interestApplied = Math.Min(appliedAmount, installment.InterestAmount);
            return Math.Min(installment.PrincipalAmount, Math.Max(0, appliedAmount - interestApplied));
        });
        var extraordinaryPrincipalPaid = loan.Payments
            .Where(payment => payment.Type == PaymentType.ExtraordinaryPrincipal)
            .Sum(payment => payment.AmountPaid);

        return new PaidBreakdown(
            Math.Round(Math.Min(loan.PrincipalAmount, paidPrincipalFromInstallments + extraordinaryPrincipalPaid), 2),
            Math.Round(Math.Min(loan.TotalInterest, paidInterest), 2));
    }

    private static PaidBreakdown SumPaidBreakdown(IEnumerable<Loan> loans)
    {
        var breakdowns = loans.Select(GetPaidBreakdown).ToList();
        return new PaidBreakdown(
            Math.Round(breakdowns.Sum(item => item.Principal), 2),
            Math.Round(breakdowns.Sum(item => item.Interest), 2));
    }

    private sealed record PaidBreakdown(decimal Principal, decimal Interest);

    private static InstallmentStatus GetEffectiveInstallmentStatus(Installment installment)
    {
        if (installment.AmountPaid >= installment.PaymentAmount)
        {
            return InstallmentStatus.Paid;
        }

        if (installment.DueDate.Date < BusinessClock.Today)
        {
            return InstallmentStatus.Overdue;
        }

        return installment.AmountPaid > 0
            ? InstallmentStatus.Partial
            : InstallmentStatus.Pending;
    }

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
                .Select(charge => charge.ToDto(loan))
                .ToList());

    public static PaymentDto ToDto(this Payment payment)
        => new(
            payment.Id,
            payment.LoanId,
            payment.InstallmentId,
            payment.LoanChargeId,
            payment.PaymentDate,
            payment.AmountPaid,
            payment.Type,
            payment.PaymentMethod,
            payment.ReferenceNumber,
            payment.Notes,
            payment.ReceiptId,
            payment.Receipt?.FileName,
            payment.RecalculationMode,
            payment.PreviousOutstandingPrincipal,
            payment.NewOutstandingPrincipal,
            payment.PreviousInstallmentAmount,
            payment.NewInstallmentAmount,
            payment.PreviousInstallmentCount,
            payment.NewInstallmentCount,
            payment.PreviousPendingInterest,
            payment.NewPendingInterest);
}
