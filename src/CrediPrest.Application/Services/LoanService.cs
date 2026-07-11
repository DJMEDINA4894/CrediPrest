using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CrediPrest.Application.Services;

internal sealed class LoanService(
    IApplicationDbContext dbContext,
    ICurrentUserContext currentUser,
    ILogger<LoanService> logger) : ILoanService
{
    private static readonly Regex MoneyAmountRegex = new(@"(?<amount>\d+(?:[.,]\d{1,2})?)", RegexOptions.Compiled);

    public async Task<IReadOnlyList<LoanDto>> ListAsync(string? status, CancellationToken cancellationToken = default)
    {
        await RefreshOverdueAsync(cancellationToken);

        var query = ApplyOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Charges)
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
            ReferenceName = NormalizeOptional(request.ReferenceName),
            Notes = NormalizeOptional(request.Notes),
            AgreementCity = NormalizeOptional(request.AgreementCity),
            LateFeeDescription = NormalizeOptional(request.LateFeeDescription)
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
            var requiresRecalculation = RequiresRecalculation(loan, request);
            if (loan.Payments.Count != 0 && requiresRecalculation)
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
            loan.ReferenceName = NormalizeOptional(request.ReferenceName);
            loan.Notes = NormalizeOptional(request.Notes);
            loan.AgreementCity = NormalizeOptional(request.AgreementCity);
            loan.LateFeeDescription = NormalizeOptional(request.LateFeeDescription);

            if (requiresRecalculation)
            {
                dbContext.LoanCharges.RemoveRange(loan.Charges);
                await dbContext.DeleteInstallmentsByLoanIdAsync(id, cancellationToken);
                loan.Charges.Clear();
                dbContext.Installments.AddRange(RecalculateLoan(loan));
            }

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
            .Include(item => item.Charges)
            .FirstOrDefaultAsync(item => item.Id == id && item.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

        await using var transaction = await BeginTransactionIfAvailableAsync(cancellationToken);

        var installmentIds = loan.Installments.Select(installment => installment.Id).ToList();
        var payments = await dbContext.Payments
            .Where(payment => payment.LoanId == id
                || (payment.InstallmentId.HasValue && installmentIds.Contains(payment.InstallmentId.Value)))
            .ToListAsync(cancellationToken);
        var receiptIds = payments
            .Where(payment => payment.ReceiptId.HasValue)
            .Select(payment => payment.ReceiptId!.Value)
            .Distinct()
            .ToList();

        dbContext.Payments.RemoveRange(payments);
        if (receiptIds.Count > 0)
        {
            var receipts = await dbContext.PaymentReceipts
                .Where(receipt => receiptIds.Contains(receipt.Id))
                .ToListAsync(cancellationToken);
            dbContext.PaymentReceipts.RemoveRange(receipts);
        }
        dbContext.LoanCharges.RemoveRange(loan.Charges);
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
        await LoanDataOperationLock.Gate.WaitAsync(cancellationToken);
        IDbContextTransaction? transaction = null;
        try
        {
            if (dbContext is DbContext databaseContext)
            {
                transaction = await databaseContext.Database.BeginTransactionAsync(cancellationToken);
                await databaseContext.Database.ExecuteSqlRawAsync(
                    $"EXEC sp_getapplock @Resource = N'{LoanDataOperationLock.ResourceName}', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;",
                    cancellationToken);
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await RefreshOverdueOnceAsync(cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 2)
                {
                    if (dbContext is DbContext trackedContext)
                    {
                        foreach (var entry in trackedContext.ChangeTracker.Entries().ToList())
                        {
                            entry.State = EntityState.Detached;
                        }
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    logger.LogWarning(
                        exception,
                        "No se pudo actualizar el estado automático de las cuotas después de varios intentos; se continuará con la consulta.");

                    if (dbContext is DbContext trackedContext)
                    {
                        foreach (var entry in trackedContext.ChangeTracker.Entries().ToList())
                        {
                            entry.State = EntityState.Detached;
                        }
                    }

                    return;
                }
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            LoanDataOperationLock.Gate.Release();
        }
    }

    private async Task RefreshOverdueOnceAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var loans = await dbContext.Loans
            .Include(loan => loan.Client)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Charges)
            .Where(loan => loan.Client.IsActive && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue))
            .ToListAsync(cancellationToken);

        foreach (var loan in loans)
        {
            ApplyLateFees(loan, today);

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
            var hasPendingCharges = loan.Charges.Any(charge => charge.AmountPaid < charge.Amount);
            var isPaid = loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid) && !hasPendingCharges;
            loan.Status = isPaid ? LoanStatus.Cancelled : hasOverdue || hasPendingCharges ? LoanStatus.Overdue : LoanStatus.Active;
        }

        if (dbContext is DbContext efContext && !efContext.ChangeTracker.HasChanges())
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Loan> LoadLoanAsync(Guid id, CancellationToken cancellationToken)
        => await ApplyOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Charges)
            .FirstOrDefaultAsync(loan => loan.Id == id && loan.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

    private async Task<Loan> LoadLoanForUpdateAsync(Guid id, CancellationToken cancellationToken)
        => await ApplyOwnershipFilter(dbContext.Loans)
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Charges)
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

    private static void ApplyLateFees(Loan loan, DateTime today)
    {
        if (!TryReadLateFeeAmount(loan.LateFeeDescription, out var lateFeeAmount))
        {
            return;
        }

        var blockSize = loan.PaymentFrequency switch
        {
            PaymentFrequency.Weekly => 4,
            PaymentFrequency.Biweekly => 2,
            _ => 1
        };

        var periods = loan.Installments
            .OrderBy(installment => installment.InstallmentNumber)
            .GroupBy(installment => ((installment.InstallmentNumber - 1) / blockSize) + 1);

        foreach (var period in periods)
        {
            var installments = period.ToList();
            var periodStart = installments.Min(installment => installment.DueDate.Date);
            var periodEnd = loan.PaymentFrequency switch
            {
                PaymentFrequency.Weekly => periodStart.AddDays(28),
                PaymentFrequency.Biweekly => periodStart.AddDays(30),
                PaymentFrequency.Monthly => periodStart.AddMonths(1),
                _ => periodStart.AddMonths(1)
            };

            if (today < periodEnd || installments.All(installment => installment.AmountPaid >= installment.PaymentAmount))
            {
                continue;
            }

            var alreadyApplied = loan.Charges.Any(charge =>
                charge.Type == LoanChargeType.LateFee && charge.PeriodNumber == period.Key);
            if (alreadyApplied)
            {
                continue;
            }

            loan.Charges.Add(new LoanCharge
            {
                Loan = loan,
                LoanId = loan.Id,
                Type = LoanChargeType.LateFee,
                PeriodNumber = period.Key,
                PeriodStartDate = periodStart,
                PeriodEndDate = periodEnd,
                Amount = lateFeeAmount,
                Notes = $"Mora por atraso del periodo {period.Key}."
            });
        }
    }

    private static bool TryReadLateFeeAmount(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = MoneyAmountRegex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        var normalized = match.Groups["amount"].Value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;
    }

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

    private static bool RequiresRecalculation(Loan loan, UpdateLoanRequest request)
        => loan.PrincipalAmount != request.PrincipalAmount
            || loan.Currency != request.Currency
            || loan.MonthlyInterestRate != request.MonthlyInterestRate
            || loan.TermMonths != request.TermMonths
            || loan.PaymentFrequency != request.PaymentFrequency
            || loan.StartDate.Date != request.StartDate.Date;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

internal static class LoanDataOperationLock
{
    public const string ResourceName = "CrediPrest.LoanDataOperation";
    public static readonly SemaphoreSlim Gate = new(1, 1);
}
