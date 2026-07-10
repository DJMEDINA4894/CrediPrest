using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrediPrest.Application.Services;

internal sealed class LoanService(IApplicationDbContext dbContext, ICurrentUserContext currentUser) : ILoanService
{
    public async Task<IReadOnlyList<LoanDto>> ListAsync(string? status, CancellationToken cancellationToken = default)
    {
        await RefreshOverdueAsync(cancellationToken);

        var query = ApplyOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Installments)
            .Where(loan => loan.Client.IsActive)
            .AsQueryable();

        if (Enum.TryParse<LoanStatus>(status, true, out var loanStatus))
        {
            query = query.Where(loan => loan.Status == loanStatus);
        }

        var loans = await query
            .OrderByDescending(loan => loan.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return loans.Select(loan => loan.ToDto()).ToList();
    }

    public async Task<LoanDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await RefreshOverdueAsync(cancellationToken);
        return (await LoadLoanAsync(id, cancellationToken)).ToDetailDto();
    }

    public async Task<LoanDetailDto> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken = default)
    {
        ValidateLoan(request.PrincipalAmount, request.MonthlyInterestRate, request.TermMonths);

        var client = await ApplyClientOwnershipFilter(dbContext.Clients)
            .FirstOrDefaultAsync(client => client.Id == request.ClientId && client.IsActive, cancellationToken);

        if (client is null)
        {
            throw new KeyNotFoundException("Cliente activo no encontrado.");
        }

        var loan = new Loan
        {
            ClientId = request.ClientId,
            LenderUserId = currentUser.IsLender ? currentUser.UserId : client.LenderUserId,
            PrincipalAmount = request.PrincipalAmount,
            Currency = request.Currency,
            MonthlyInterestRate = request.MonthlyInterestRate,
            TermMonths = request.TermMonths,
            PaymentFrequency = request.PaymentFrequency,
            StartDate = request.StartDate.Date,
            EndDate = GetEndDate(request.StartDate.Date, request.PaymentFrequency, request.TermMonths),
            ReferenceName = request.ReferenceName?.Trim(),
            Notes = request.Notes?.Trim()
        };

        loan.Installments.AddRange(RecalculateLoan(loan));
        dbContext.Loans.Add(loan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (await LoadLoanAsync(loan.Id, cancellationToken)).ToDetailDto();
    }

    public async Task<LoanDetailDto> UpdateAsync(Guid id, UpdateLoanRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateLoan(request.PrincipalAmount, request.MonthlyInterestRate, request.TermMonths);

            var loan = await LoadLoanForUpdateAsync(id, cancellationToken);
            if (loan.Payments.Count != 0)
            {
                throw new InvalidOperationException("No se puede recalcular un préstamo que ya tiene pagos registrados.");
            }

            await using var transaction = await BeginTransactionIfAvailableAsync(cancellationToken);

            loan.PrincipalAmount = request.PrincipalAmount;
            loan.Currency = request.Currency;
            loan.MonthlyInterestRate = request.MonthlyInterestRate;
            loan.TermMonths = request.TermMonths;
            loan.PaymentFrequency = request.PaymentFrequency;
            loan.StartDate = request.StartDate.Date;
            loan.EndDate = GetEndDate(request.StartDate.Date, request.PaymentFrequency, request.TermMonths);
            loan.Status = request.Status;
            loan.ReferenceName = request.ReferenceName?.Trim();
            loan.Notes = request.Notes?.Trim();

            await dbContext.DeleteInstallmentsByLoanIdAsync(id, cancellationToken);

            dbContext.Installments.AddRange(RecalculateLoan(loan));
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (await LoadLoanAsync(id, cancellationToken)).ToDetailDto();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("No se pudo guardar el préstamo porque sus cuotas cambiaron mientras se editaba. Actualiza la pantalla e intenta de nuevo.", exception);
        }
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(id, cancellationToken);
        loan.Status = LoanStatus.Cancelled;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var loan = await ApplyOwnershipFilter(dbContext.Loans)
            .Include(item => item.Client)
            .Include(item => item.Installments)
            .Include(item => item.Payments)
            .FirstOrDefaultAsync(item => item.Id == id && item.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

        await using var transaction = await BeginTransactionIfAvailableAsync(cancellationToken);

        var installmentIds = loan.Installments.Select(installment => installment.Id).ToList();
        var payments = await dbContext.Payments
            .Where(payment => payment.LoanId == id || installmentIds.Contains(payment.InstallmentId))
            .ToListAsync(cancellationToken);

        dbContext.Payments.RemoveRange(payments);
        dbContext.Installments.RemoveRange(loan.Installments);
        dbContext.Loans.Remove(loan);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task RefreshOverdueAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var loans = await dbContext.Loans
            .Include(loan => loan.Client)
            .Include(loan => loan.Installments)
            .Where(loan => loan.Client.IsActive && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue))
            .ToListAsync(cancellationToken);

        foreach (var loan in loans)
        {
            foreach (var installment in loan.Installments)
            {
                if (installment.Status is InstallmentStatus.Paid)
                {
                    continue;
                }

                installment.Status = installment.DueDate.Date < today
                    ? InstallmentStatus.Overdue
                    : InstallmentStatus.Pending;
            }

            var hasOverdue = loan.Installments.Any(installment => installment.Status == InstallmentStatus.Overdue);
            var isPaid = loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid);
            loan.Status = isPaid ? LoanStatus.Cancelled : hasOverdue ? LoanStatus.Overdue : LoanStatus.Active;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Loan> LoadLoanAsync(Guid id, CancellationToken cancellationToken)
        => await ApplyOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .FirstOrDefaultAsync(loan => loan.Id == id && loan.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

    private async Task<Loan> LoadLoanForUpdateAsync(Guid id, CancellationToken cancellationToken)
        => await ApplyOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Payments)
            .FirstOrDefaultAsync(loan => loan.Id == id && loan.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

    private async Task<IDbContextTransaction?> BeginTransactionIfAvailableAsync(CancellationToken cancellationToken)
        => dbContext is DbContext efContext
            ? await efContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private IQueryable<Loan> ApplyOwnershipFilter(IQueryable<Loan> query)
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

    private static IReadOnlyList<Installment> RecalculateLoan(Loan loan)
    {
        var installmentCount = GetInstallmentCount(loan.TermMonths, loan.PaymentFrequency);
        var interestMonths = GetInterestMonthFactor(loan.TermMonths, loan.PaymentFrequency);
        loan.TotalInterest = Math.Round(loan.PrincipalAmount * (loan.MonthlyInterestRate / 100m) * interestMonths, 2);
        loan.TotalToPay = loan.PrincipalAmount + loan.TotalInterest;
        var installments = new List<Installment>();

        var basePrincipal = Math.Round(loan.PrincipalAmount / installmentCount, 2);
        var baseInterest = Math.Round(loan.TotalInterest / installmentCount, 2);
        var basePayment = basePrincipal + baseInterest;
        var principalAllocated = 0m;
        var interestAllocated = 0m;

        for (var index = 1; index <= installmentCount; index++)
        {
            var principal = index == installmentCount
                ? loan.PrincipalAmount - principalAllocated
                : basePrincipal;
            var interest = index == installmentCount
                ? loan.TotalInterest - interestAllocated
                : baseInterest;

            principal = Math.Round(principal, 2);
            interest = Math.Round(interest, 2);
            principalAllocated += principal;
            interestAllocated += interest;

            installments.Add(new Installment
            {
                Loan = loan,
                InstallmentNumber = index,
                DueDate = GetDueDate(loan.StartDate, loan.PaymentFrequency, index),
                PrincipalAmount = principal,
                InterestAmount = interest,
                PaymentAmount = index == installmentCount ? principal + interest : basePayment,
                RemainingBalance = Math.Max(0, Math.Round(loan.PrincipalAmount - principalAllocated, 2))
            });
        }

        return installments;
    }

    private static int GetInstallmentCount(int termMonths, PaymentFrequency frequency)
        => termMonths;

    private static decimal GetInterestMonthFactor(int termMonths, PaymentFrequency frequency)
        => frequency switch
        {
            PaymentFrequency.Weekly => termMonths / 4m,
            PaymentFrequency.Biweekly => termMonths / 2m,
            PaymentFrequency.Monthly => termMonths,
            _ => termMonths
        };

    private static DateTime GetEndDate(DateTime startDate, PaymentFrequency frequency, int termMonths)
        => GetDueDate(startDate, frequency, termMonths);

    private static DateTime GetDueDate(DateTime startDate, PaymentFrequency frequency, int installmentNumber)
        => frequency switch
        {
            PaymentFrequency.Weekly => startDate.Date.AddDays(7 * installmentNumber),
            PaymentFrequency.Biweekly => startDate.Date.AddDays(15 * installmentNumber),
            PaymentFrequency.Monthly => startDate.Date.AddMonths(installmentNumber),
            _ => startDate.Date.AddMonths(installmentNumber)
        };

    private static void ValidateLoan(decimal principalAmount, decimal monthlyInterestRate, int termMonths)
    {
        if (principalAmount <= 0)
        {
            throw new InvalidOperationException("El monto prestado debe ser mayor que cero.");
        }

        if (monthlyInterestRate < 0)
        {
            throw new InvalidOperationException("La tasa de interés no puede ser negativa.");
        }

        if (termMonths <= 0)
        {
            throw new InvalidOperationException("La cantidad de pagos debe ser mayor que cero.");
        }
    }
}
