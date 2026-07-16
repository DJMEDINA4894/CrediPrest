using System.Globalization;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.Services;

internal static class LateFeeCalculator
{
    public static IReadOnlyList<LateFeePeriod> BuildPeriods(Loan loan)
    {
        var blockSize = loan.PaymentFrequency switch
        {
            PaymentFrequency.Weekly => 4,
            PaymentFrequency.Biweekly => 2,
            _ => 1
        };

        return loan.Installments
            .OrderBy(installment => installment.InstallmentNumber)
            .GroupBy(installment => ((installment.InstallmentNumber - 1) / blockSize) + 1)
            .Select(group =>
            {
                var installments = group.OrderBy(item => item.InstallmentNumber).ToList();
                var startDate = installments.Min(item => item.DueDate.Date);
                var endDate = installments.Max(item => item.DueDate.Date);
                return new LateFeePeriod(group.Key, installments, startDate, endDate);
            })
            .ToList();
    }

    public static LateFeeCalculation Calculate(
        Loan loan,
        IReadOnlyList<Installment> installments,
        DateTime calculationDate)
    {
        var percentage = ReadPercentage(loan.LateFeeDescription);
        var monthlyRate = loan.MonthlyInterestRate * percentage / 100m;
        var overdueInstallments = installments
            .Where(installment => installment.DueDate.Date < calculationDate.Date)
            .Select(installment => new
            {
                Installment = installment,
                PendingAmount = GetAmountPendingAtDueDate(loan, installment)
            })
            .Where(item => item.PendingAmount > 0)
            .ToList();
        var pendingPeriodAmount = overdueInstallments.Sum(item => item.PendingAmount);
        var lateFeeAmount = Math.Round(pendingPeriodAmount * monthlyRate / 100m, 2);
        var allocations = new List<LateFeeInstallmentAllocation>();
        var allocatedAmount = 0m;

        for (var index = 0; index < overdueInstallments.Count; index++)
        {
            var item = overdueInstallments[index];
            var isLast = index == overdueInstallments.Count - 1;
            var amount = isLast
                ? lateFeeAmount - allocatedAmount
                : pendingPeriodAmount > 0
                    ? Math.Round(lateFeeAmount * item.PendingAmount / pendingPeriodAmount, 2)
                    : 0m;
            amount = Math.Max(0, amount);
            allocatedAmount += amount;
            allocations.Add(new LateFeeInstallmentAllocation(item.Installment.Id, amount));
        }

        return new LateFeeCalculation(
            lateFeeAmount,
            percentage,
            monthlyRate,
            pendingPeriodAmount,
            overdueInstallments.Count == 0
                ? null
                : overdueInstallments.Max(item => item.Installment.DueDate.Date),
            allocations);
    }

    private static decimal GetAmountPendingAtDueDate(Loan loan, Installment installment)
    {
        var linkedPayments = loan.Payments
            .Where(payment => payment.Type == PaymentType.Regular
                && payment.InstallmentId == installment.Id)
            .ToList();
        var amountPaidByDueDate = linkedPayments.Count == 0
            ? Math.Min(installment.PaymentAmount, Math.Max(0, installment.AmountPaid))
            : linkedPayments
                .Where(payment => payment.PaymentDate.Date <= installment.DueDate.Date)
                .Sum(payment => payment.AmountPaid);

        return Math.Max(0, installment.PaymentAmount - amountPaidByDueDate);
    }

    public static decimal ReadPercentage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 50m;
        }

        var normalized = value.Replace("%", string.Empty).Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage)
            && percentage >= 0
            && percentage <= 100
                ? percentage
                : 0m;
    }

}

internal sealed record LateFeePeriod(
    int Number,
    IReadOnlyList<Installment> Installments,
    DateTime StartDate,
    DateTime EndDate);

internal sealed record LateFeeCalculation(
    decimal Amount,
    decimal Percentage,
    decimal MonthlyRate,
    decimal PendingPeriodAmount,
    DateTime? EligibleThroughDate,
    IReadOnlyList<LateFeeInstallmentAllocation> Allocations);

internal sealed record LateFeeInstallmentAllocation(
    Guid InstallmentId,
    decimal Amount);
