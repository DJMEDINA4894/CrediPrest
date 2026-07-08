using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Dashboard;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class DashboardService(IApplicationDbContext dbContext, ILoanService loanService) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        await loanService.RefreshOverdueAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var weekEnd = today.AddDays(7);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var loans = await dbContext.Loans
            .Include(loan => loan.Client)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Where(loan => loan.Client.IsActive)
            .ToListAsync(cancellationToken);
        var clients = await dbContext.Clients.ToListAsync(cancellationToken);
        var payments = await dbContext.Payments
            .Include(payment => payment.Installment)
            .Include(payment => payment.Loan)
            .ThenInclude(loan => loan.Client)
            .Where(payment => payment.Loan.Client.IsActive)
            .ToListAsync(cancellationToken);

        var totalRecoveredCordobas = loans
            .Where(loan => loan.Currency == CurrencyType.Cordoba)
            .Sum(GetAppliedInstallmentAmount);
        var totalRecoveredUsd = loans
            .Where(loan => loan.Currency == CurrencyType.Usd)
            .Sum(GetAppliedInstallmentAmount);
        var interestCollectedCordobas = loans
            .Where(loan => loan.Currency == CurrencyType.Cordoba)
            .Sum(GetCollectedInterestAmount);
        var interestCollectedUsd = loans
            .Where(loan => loan.Currency == CurrencyType.Usd)
            .Sum(GetCollectedInterestAmount);

        return new DashboardDto(
            TotalLoanedCordobas: loans.Where(loan => loan.Currency == CurrencyType.Cordoba).Sum(loan => loan.PrincipalAmount),
            TotalLoanedUsd: loans.Where(loan => loan.Currency == CurrencyType.Usd).Sum(loan => loan.PrincipalAmount),
            TotalRecoveredCordobas: totalRecoveredCordobas,
            TotalRecoveredUsd: totalRecoveredUsd,
            PendingCordobas: loans.Where(loan => loan.Currency == CurrencyType.Cordoba).Sum(loan => Math.Max(0, loan.TotalToPay - GetAppliedInstallmentAmount(loan))),
            PendingUsd: loans.Where(loan => loan.Currency == CurrencyType.Usd).Sum(loan => Math.Max(0, loan.TotalToPay - GetAppliedInstallmentAmount(loan))),
            EstimatedInterestCordobas: loans.Where(loan => loan.Currency == CurrencyType.Cordoba).Sum(loan => loan.TotalInterest),
            EstimatedInterestUsd: loans.Where(loan => loan.Currency == CurrencyType.Usd).Sum(loan => loan.TotalInterest),
            RealInterestCollectedCordobas: interestCollectedCordobas,
            RealInterestCollectedUsd: interestCollectedUsd,
            ActiveClients: clients.Count(client => client.IsActive),
            ActiveLoans: loans.Count(loan => loan.Status == LoanStatus.Active),
            OverdueLoans: loans.Count(loan => loan.Status == LoanStatus.Overdue),
            OverdueInstallments: loans.SelectMany(loan => loan.Installments).Count(installment => installment.Status == InstallmentStatus.Overdue),
            DueTodayInstallments: loans.SelectMany(loan => loan.Installments).Count(installment => installment.DueDate.Date == today && installment.Status != InstallmentStatus.Paid),
            DueThisWeekInstallments: loans.SelectMany(loan => loan.Installments).Count(installment => installment.DueDate.Date >= today && installment.DueDate.Date <= weekEnd && installment.Status != InstallmentStatus.Paid),
            PaidTodayCount: payments.Count(payment => payment.PaymentDate.Date == today),
            PaidThisWeekCount: payments.Count(payment => payment.PaymentDate.Date >= today.AddDays(-7) && payment.PaymentDate.Date <= today),
            PaidThisMonthCount: payments.Count(payment => payment.PaymentDate.Date >= monthStart && payment.PaymentDate.Date < monthEnd));
    }

    private static decimal GetAppliedInstallmentAmount(Domain.Entities.Loan loan)
        => Math.Min(loan.TotalToPay, loan.Installments.Sum(installment => installment.AmountPaid));

    private static decimal GetCollectedInterestAmount(Domain.Entities.Loan loan)
        => loan.Installments.Sum(installment => Math.Min(installment.AmountPaid, installment.InterestAmount));
}
