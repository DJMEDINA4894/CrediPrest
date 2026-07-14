using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Dashboard;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class DashboardService(IApplicationDbContext dbContext, ICurrentUserContext currentUser) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var weekEnd = today.AddDays(7);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var loans = await ApplyLoanOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Charges)
            .Where(loan => loan.Client.IsActive)
            .ToListAsync(cancellationToken);
        var clients = await ApplyClientOwnershipFilter(dbContext.Clients).ToListAsync(cancellationToken);
        var payments = await dbContext.Payments
            .Include(payment => payment.Installment)
            .Include(payment => payment.Loan)
            .ThenInclude(loan => loan.Client)
            .Where(payment => payment.Loan.Client.IsActive)
            .ToListAsync(cancellationToken);
        if (currentUser.IsLender && currentUser.UserId.HasValue)
        {
            payments = payments.Where(payment => payment.Loan.LenderUserId == currentUser.UserId.Value).ToList();
        }

        var todayPayments = payments.Where(payment => payment.PaymentDate.Date == today).ToList();
        var weekPayments = payments.Where(payment => payment.PaymentDate.Date >= today.AddDays(-7) && payment.PaymentDate.Date <= today).ToList();
        var monthPayments = payments.Where(payment => payment.PaymentDate.Date >= monthStart && payment.PaymentDate.Date < monthEnd).ToList();

        var totalRecoveredCordobas = SumRecoveredByCurrency(loans, CurrencyType.Cordoba);
        var totalRecoveredUsd = SumRecoveredByCurrency(loans, CurrencyType.Usd);

        return new DashboardDto(
            TotalLoanedCordobas: loans.Where(loan => loan.Currency == CurrencyType.Cordoba).Sum(loan => loan.PrincipalAmount),
            TotalLoanedUsd: loans.Where(loan => loan.Currency == CurrencyType.Usd).Sum(loan => loan.PrincipalAmount),
            TotalRecoveredCordobas: totalRecoveredCordobas,
            TotalRecoveredUsd: totalRecoveredUsd,
            PendingCordobas: loans.Where(loan => loan.Currency == CurrencyType.Cordoba).Sum(GetPendingAmount),
            PendingUsd: loans.Where(loan => loan.Currency == CurrencyType.Usd).Sum(GetPendingAmount),
            EstimatedInterestCordobas: loans.Where(loan => loan.Currency == CurrencyType.Cordoba).Sum(loan => loan.TotalInterest),
            EstimatedInterestUsd: loans.Where(loan => loan.Currency == CurrencyType.Usd).Sum(loan => loan.TotalInterest),
            ActiveClients: clients.Count(client => client.IsActive),
            ActiveLoans: loans.Count(loan => loan.Status == LoanStatus.Active),
            OverdueLoans: loans.Count(loan => loan.Status == LoanStatus.Overdue),
            OverdueInstallments: loans.SelectMany(loan => loan.Installments).Count(installment => installment.Status == InstallmentStatus.Overdue),
            DueTodayInstallments: loans.SelectMany(loan => loan.Installments).Count(installment => installment.DueDate.Date == today && installment.Status != InstallmentStatus.Paid),
            DueThisWeekInstallments: loans.SelectMany(loan => loan.Installments).Count(installment => installment.DueDate.Date >= today && installment.DueDate.Date <= weekEnd && installment.Status != InstallmentStatus.Paid),
            PaidTodayCordobas: SumPaymentsByCurrency(todayPayments, CurrencyType.Cordoba),
            PaidTodayUsd: SumPaymentsByCurrency(todayPayments, CurrencyType.Usd),
            PaidThisWeekCordobas: SumPaymentsByCurrency(weekPayments, CurrencyType.Cordoba),
            PaidThisWeekUsd: SumPaymentsByCurrency(weekPayments, CurrencyType.Usd),
            PaidThisMonthCordobas: SumPaymentsByCurrency(monthPayments, CurrencyType.Cordoba),
            PaidThisMonthUsd: SumPaymentsByCurrency(monthPayments, CurrencyType.Usd),
            PaidTodayCount: todayPayments.Count,
            PaidThisWeekCount: weekPayments.Count,
            PaidThisMonthCount: monthPayments.Count);
    }

    private static decimal GetAppliedInstallmentAmount(Domain.Entities.Loan loan)
        => Math.Min(loan.TotalToPay, loan.Installments.Sum(installment => installment.AmountPaid));

    private static decimal GetRecoveredAmount(Domain.Entities.Loan loan)
        => GetAppliedInstallmentAmount(loan)
            + loan.Charges.Sum(charge => Math.Min(charge.Amount, Math.Max(0, charge.AmountPaid)));

    private static decimal GetPendingAmount(Domain.Entities.Loan loan)
        => Math.Max(0, loan.TotalToPay - GetAppliedInstallmentAmount(loan))
            + loan.Charges.Sum(charge => Math.Max(0, charge.Amount - charge.AmountPaid));

    private static decimal SumPaymentsByCurrency(IEnumerable<Domain.Entities.Payment> payments, CurrencyType currency)
        => payments.Where(payment => payment.Loan.Currency == currency).Sum(payment => payment.AmountPaid);

    private static decimal SumRecoveredByCurrency(IEnumerable<Domain.Entities.Loan> loans, CurrencyType currency)
        => loans.Where(loan => loan.Currency == currency).Sum(GetRecoveredAmount);

    private IQueryable<Domain.Entities.Loan> ApplyLoanOwnershipFilter(IQueryable<Domain.Entities.Loan> query)
    {
        if (!currentUser.IsLender || !currentUser.UserId.HasValue)
        {
            return query;
        }

        return query.Where(loan => loan.LenderUserId == currentUser.UserId.Value);
    }

    private IQueryable<Domain.Entities.Client> ApplyClientOwnershipFilter(IQueryable<Domain.Entities.Client> query)
    {
        if (!currentUser.IsLender || !currentUser.UserId.HasValue)
        {
            return query;
        }

        return query.Where(client => client.LenderUserId == currentUser.UserId.Value);
    }
}
