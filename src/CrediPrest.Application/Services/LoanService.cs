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
        => (await LoadLoanAsync(id, cancellationToken)).ToDetailDto();

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
            LateFeeDescription = NormalizeLateFeePercentage(request.LateFeeDescription)
        };

        loan.Installments.AddRange(RecalculateLoan(loan));
        dbContext.Loans.Add(loan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (await LoadLoanAsync(loan.Id, cancellationToken)).ToDetailDto();
    }

    public async Task<LoanDetailDto> UpdateAsync(Guid id, UpdateLoanRequest request, CancellationToken cancellationToken = default)
    {
        await LoanDataOperationLock.Gate.WaitAsync(cancellationToken);
        try
        {
            ValidateLoan(request.PrincipalAmount, request.MonthlyInterestRate, request.TermMonths);
            var normalizedLateFee = NormalizeLateFeePercentage(request.LateFeeDescription);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await using var transaction = await BeginTransactionIfAvailableAsync(cancellationToken);
                    if (transaction is not null && dbContext is DbContext databaseContext)
                    {
                        await databaseContext.Database.ExecuteSqlRawAsync(
                            $"EXEC sp_getapplock @Resource = N'{LoanDataOperationLock.ResourceName}', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;",
                            cancellationToken);
                    }

                    var loan = await LoadLoanForUpdateAsync(id, cancellationToken);
                    var previousLateFee = NormalizeLateFeePercentage(loan.LateFeeDescription);
                    var lateFeeChanged = previousLateFee != normalizedLateFee;
                    var requiresRecalculation = RequiresRecalculation(loan, request);
                    if (loan.Payments.Count != 0 && requiresRecalculation)
                    {
                        throw new InvalidOperationException("No se puede recalcular un préstamo que ya tiene pagos registrados.");
                    }

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
                    loan.LateFeeDescription = normalizedLateFee;

                    if (requiresRecalculation)
                    {
                        dbContext.LoanCharges.RemoveRange(loan.Charges);
                        loan.Charges.Clear();
                        dbContext.Installments.RemoveRange(loan.Installments);
                        loan.Installments.Clear();
                        dbContext.Installments.AddRange(RecalculateLoan(loan));
                    }

                    var newCharges = ApplyLateFees(loan, BusinessClock.Today, recalculateExistingCharges: true);
                    dbContext.LoanCharges.AddRange(newCharges);
                    if (lateFeeChanged)
                    {
                        await UpsertLateFeeRateChangedNotificationsAsync(
                            loan,
                            previousLateFee,
                            normalizedLateFee,
                            cancellationToken);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return (await LoadLoanAsync(id, cancellationToken)).ToDetailDto();
                }
                catch (DbUpdateConcurrencyException exception) when (attempt < 2)
                {
                    logger.LogWarning(
                        exception,
                        "Se detectó una actualización simultánea del préstamo {LoanId}; se volverá a cargar antes de reintentar ({Attempt}/3).",
                        id,
                        attempt + 1);

                    DetachTrackedEntities();
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                }
            }

            throw new InvalidOperationException("No se pudo guardar el préstamo porque sus cuotas cambiaron mientras se editaba. Actualiza la pantalla e intenta de nuevo.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("No se pudo guardar el préstamo porque sus cuotas cambiaron mientras se editaba. Actualiza la pantalla e intenta de nuevo.", exception);
        }
        finally
        {
            LoanDataOperationLock.Gate.Release();
        }
    }

    public async Task<LoanRecalculationPreviewDto> PreviewExtraordinaryPaymentAsync(
        Guid id,
        ExtraordinaryPaymentPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var loan = await LoadLoanAsync(id, cancellationToken);
        return BuildExtraordinaryPaymentPlan(
            loan,
            request.Mode,
            request.EffectiveDate,
            request.Amount,
            request.NewInstallmentCount).Preview;
    }

    public async Task<LoanDetailDto> RegisterExtraordinaryPaymentAsync(
        Guid id,
        RegisterExtraordinaryPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.PaymentMethod))
        {
            throw new InvalidOperationException("Selecciona un método de pago válido.");
        }

        if (request.PaymentMethod is PaymentMethod.Transfer or PaymentMethod.Deposit or PaymentMethod.Kash
            && string.IsNullOrWhiteSpace(request.ReferenceNumber))
        {
            throw new InvalidOperationException("Ingresa la referencia de la transferencia, depósito o Kash.");
        }

        await LoanDataOperationLock.Gate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await BeginTransactionIfAvailableAsync(cancellationToken);
            if (transaction is not null && dbContext is DbContext databaseContext)
            {
                await databaseContext.Database.ExecuteSqlRawAsync(
                    $"EXEC sp_getapplock @Resource = N'{LoanDataOperationLock.ResourceName}', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;",
                    cancellationToken);
            }

            var loan = await LoadLoanForUpdateAsync(id, cancellationToken);
            var plan = BuildExtraordinaryPaymentPlan(
                loan,
                request.Mode,
                request.EffectiveDate,
                request.Amount,
                request.NewInstallmentCount);
            var replacedInstallmentIds = plan.InstallmentsToReplace.Select(installment => installment.Id).ToArray();
            if (replacedInstallmentIds.Length > 0)
            {
                var obsoleteNotifications = await dbContext.Notifications
                    .Where(notification => replacedInstallmentIds.Contains(notification.RelatedEntityId))
                    .ToListAsync(cancellationToken);
                dbContext.Notifications.RemoveRange(obsoleteNotifications);
            }

            dbContext.Installments.RemoveRange(plan.InstallmentsToReplace);
            foreach (var installment in plan.InstallmentsToReplace)
            {
                loan.Installments.Remove(installment);
            }

            var extraordinaryPayment = new Payment
            {
                Loan = loan,
                LoanId = loan.Id,
                PaymentDate = request.EffectiveDate.Date,
                AmountPaid = request.Amount,
                Type = PaymentType.ExtraordinaryPrincipal,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = NormalizeOptional(request.ReferenceNumber),
                Notes = NormalizeOptional(request.Notes),
                RecalculationMode = request.Mode,
                PreviousOutstandingPrincipal = plan.Preview.OutstandingPrincipal,
                NewOutstandingPrincipal = plan.Preview.PrincipalAfterPayment,
                PreviousInstallmentAmount = plan.Preview.CurrentInstallmentAmount,
                NewInstallmentAmount = plan.Preview.NewInstallmentAmount,
                PreviousInstallmentCount = plan.Preview.CurrentRemainingInstallments,
                NewInstallmentCount = plan.Preview.NewRemainingInstallments,
                PreviousPendingInterest = plan.Preview.CurrentPendingInterest,
                NewPendingInterest = plan.Preview.NewInterest
            };
            loan.Payments.Add(extraordinaryPayment);
            dbContext.Payments.Add(extraordinaryPayment);
            loan.Installments.AddRange(plan.NewInstallments);
            loan.TermMonths = plan.Preview.PaidInstallments + plan.Preview.NewRemainingInstallments;
            loan.TotalInterest = Math.Round(plan.PaidInterest + plan.Preview.NewInterest, 2);
            loan.TotalToPay = Math.Round(loan.PrincipalAmount + loan.TotalInterest, 2);
            loan.EndDate = plan.Preview.FirstDueDate;
            if (plan.NewInstallments.Count > 0)
            {
                loan.EndDate = plan.NewInstallments.Max(installment => installment.DueDate);
            }

            loan.Status = LoanStatus.Active;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (await LoadLoanAsync(id, cancellationToken)).ToDetailDto();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "Las cuotas cambiaron durante el abono extraordinario. Actualiza el préstamo e inténtalo nuevamente.",
                exception);
        }
        finally
        {
            LoanDataOperationLock.Gate.Release();
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
                        "No se pudo actualizar el estado automático después de varios intentos. Entidades en conflicto: {ConflictingEntities}.",
                        DescribeConcurrencyEntries(exception));

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
        var today = BusinessClock.Today;
        var loans = await dbContext.Loans
            .Include(loan => loan.Client)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Charges)
            .Where(loan => loan.Client.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var loan in loans)
        {
            if (loan.Status == LoanStatus.Cancelled
                && loan.Installments.Any(installment => installment.AmountPaid < installment.PaymentAmount))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(loan.LateFeeDescription))
            {
                loan.LateFeeDescription = "50%";
            }

            var newCharges = ApplyLateFees(loan, today);
            dbContext.LoanCharges.AddRange(newCharges);

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
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Charges)
            .FirstOrDefaultAsync(loan => loan.Id == id && loan.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

    private async Task<IDbContextTransaction?> BeginTransactionIfAvailableAsync(CancellationToken cancellationToken)
        => dbContext is DbContext efContext
            ? await efContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private void DetachTrackedEntities()
    {
        if (dbContext is not DbContext efContext)
        {
            return;
        }

        foreach (var entry in efContext.ChangeTracker.Entries().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string DescribeConcurrencyEntries(DbUpdateConcurrencyException exception)
        => string.Join(
            ", ",
            exception.Entries.Select(entry =>
            {
                var key = entry.Metadata.FindPrimaryKey();
                var keyValue = key is null
                    ? "sin-clave"
                    : string.Join(
                        "/",
                        key.Properties.Select(property => entry.Property(property.Name).CurrentValue?.ToString() ?? "null"));
                return $"{entry.Metadata.ClrType.Name}:{keyValue}";
            }));

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

    private static LoanRecalculationPlan BuildExtraordinaryPaymentPlan(
        Loan loan,
        LoanRecalculationMode mode,
        DateTime requestedEffectiveDate,
        decimal extraordinaryAmount,
        int? requestedInstallmentCount)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException("Selecciona una modalidad válida para el abono extraordinario.");
        }

        var effectiveDate = requestedEffectiveDate.Date;
        if (effectiveDate == default)
        {
            throw new InvalidOperationException("Selecciona la fecha del abono extraordinario.");
        }

        if (effectiveDate > BusinessClock.Today || effectiveDate < loan.StartDate.Date)
        {
            throw new InvalidOperationException("La fecha del abono debe estar entre la fecha de inicio del préstamo y hoy.");
        }

        if (extraordinaryAmount <= 0)
        {
            throw new InvalidOperationException("El abono extraordinario debe ser mayor que cero.");
        }

        var orderedInstallments = loan.Installments
            .OrderBy(installment => installment.InstallmentNumber)
            .ToList();
        var partiallyPaid = orderedInstallments.Any(installment =>
            installment.AmountPaid > 0 && installment.AmountPaid < installment.PaymentAmount);
        if (partiallyPaid)
        {
            throw new InvalidOperationException("Completa primero la cuota parcialmente pagada antes de realizar un abono extraordinario.");
        }

        var pendingInstallments = orderedInstallments
            .Where(installment => installment.AmountPaid < installment.PaymentAmount)
            .ToList();
        if (pendingInstallments.Count == 0)
        {
            throw new InvalidOperationException("El préstamo no tiene cuotas pendientes para aplicar el abono.");
        }

        if (pendingInstallments.Any(installment => installment.DueDate.Date < BusinessClock.Today))
        {
            throw new InvalidOperationException("Debes cancelar las cuotas vencidas antes de realizar un abono extraordinario.");
        }

        if (loan.Charges.Any(charge => charge.AmountPaid < charge.Amount))
        {
            throw new InvalidOperationException("Debes cancelar la mora pendiente antes de recalcular el préstamo.");
        }

        var paidInstallments = orderedInstallments
            .Where(installment => installment.AmountPaid >= installment.PaymentAmount)
            .ToList();
        var firstPendingInstallmentNumber = pendingInstallments.Min(installment => installment.InstallmentNumber);
        if (paidInstallments.Any(installment => installment.InstallmentNumber > firstPendingInstallmentNumber))
        {
            throw new InvalidOperationException("No se puede aplicar el abono porque existen cuotas futuras pagadas fuera del orden del plan.");
        }

        var outstandingPrincipal = Math.Round(pendingInstallments.Sum(installment => installment.PrincipalAmount), 2);
        if (outstandingPrincipal <= 0)
        {
            throw new InvalidOperationException("El préstamo no tiene capital pendiente para aplicar el abono.");
        }

        if (extraordinaryAmount >= outstandingPrincipal)
        {
            throw new InvalidOperationException("El abono debe ser menor que el capital pendiente. Para cancelarlo por completo utiliza un cierre anticipado del préstamo.");
        }

        var principalAfterPayment = Math.Round(outstandingPrincipal - extraordinaryAmount, 2);

        var currentPayment = pendingInstallments[0].PaymentAmount;
        var newInstallmentCount = mode switch
        {
            LoanRecalculationMode.LowerPayment => pendingInstallments.Count,
            LoanRecalculationMode.ShorterTerm => GetShorterInstallmentCount(
                principalAfterPayment,
                loan.MonthlyInterestRate,
                loan.PaymentFrequency,
                pendingInstallments.Count,
                currentPayment),
            LoanRecalculationMode.CustomTerm => ValidateCustomInstallmentCount(requestedInstallmentCount),
            _ => pendingInstallments.Count
        };
        var newInterest = CalculateInterest(
            principalAfterPayment,
            loan.MonthlyInterestRate,
            newInstallmentCount,
            loan.PaymentFrequency);
        var firstDueDate = GetFirstRecalculatedDueDate(
            pendingInstallments.Min(installment => installment.DueDate.Date),
            effectiveDate,
            loan.PaymentFrequency);
        var firstInstallmentNumber = firstPendingInstallmentNumber;
        var newInstallments = BuildInstallments(
            loan,
            principalAfterPayment,
            newInterest,
            newInstallmentCount,
            firstInstallmentNumber,
            firstDueDate);
        var newPayment = newInstallments[0].PaymentAmount;
        var preview = new LoanRecalculationPreviewDto(
            loan.Id,
            mode,
            effectiveDate,
            firstDueDate,
            outstandingPrincipal,
            extraordinaryAmount,
            principalAfterPayment,
            currentPayment,
            newPayment,
            paidInstallments.Count,
            pendingInstallments.Count,
            newInstallmentCount,
            pendingInstallments.Sum(installment => installment.InterestAmount),
            newInterest,
            Math.Round(pendingInstallments.Sum(installment => installment.InterestAmount) - newInterest, 2),
            Math.Round(principalAfterPayment + newInterest, 2));

        return new LoanRecalculationPlan(
            preview,
            pendingInstallments,
            newInstallments,
            paidInstallments.Sum(installment => installment.InterestAmount));
    }

    private static int ValidateCustomInstallmentCount(int? installmentCount)
    {
        if (!installmentCount.HasValue || installmentCount.Value < 1 || installmentCount.Value > 120)
        {
            throw new InvalidOperationException("La nueva cantidad de pagos debe estar entre 1 y 120.");
        }

        return installmentCount.Value;
    }

    private static int GetShorterInstallmentCount(
        decimal principal,
        decimal monthlyInterestRate,
        PaymentFrequency frequency,
        int maximumInstallments,
        decimal targetPayment)
    {
        for (var count = 1; count <= maximumInstallments; count++)
        {
            var interest = CalculateInterest(principal, monthlyInterestRate, count, frequency);
            var payment = Math.Round((principal + interest) / count, 2);
            if (payment <= targetPayment)
            {
                return count;
            }
        }

        return maximumInstallments;
    }

    private static decimal CalculateInterest(
        decimal principal,
        decimal monthlyInterestRate,
        int installmentCount,
        PaymentFrequency frequency)
        => Math.Round(
            principal * (monthlyInterestRate / 100m) * GetInterestMonthFactor(installmentCount, frequency),
            2);

    private static DateTime GetFirstRecalculatedDueDate(
        DateTime contractualDueDate,
        DateTime effectiveDate,
        PaymentFrequency frequency)
        => contractualDueDate >= effectiveDate
            ? contractualDueDate
            : GetDueDate(effectiveDate, frequency, 1);

    private static List<Installment> BuildInstallments(
        Loan loan,
        decimal principal,
        decimal interest,
        int installmentCount,
        int firstInstallmentNumber,
        DateTime firstDueDate)
    {
        var installments = new List<Installment>(installmentCount);
        var basePrincipal = Math.Round(principal / installmentCount, 2);
        var baseInterest = Math.Round(interest / installmentCount, 2);
        var principalAllocated = 0m;
        var interestAllocated = 0m;

        for (var index = 0; index < installmentCount; index++)
        {
            var isLast = index == installmentCount - 1;
            var installmentPrincipal = isLast ? principal - principalAllocated : basePrincipal;
            var installmentInterest = isLast ? interest - interestAllocated : baseInterest;
            installmentPrincipal = Math.Round(installmentPrincipal, 2);
            installmentInterest = Math.Round(installmentInterest, 2);
            principalAllocated += installmentPrincipal;
            interestAllocated += installmentInterest;

            installments.Add(new Installment
            {
                Loan = loan,
                LoanId = loan.Id,
                InstallmentNumber = firstInstallmentNumber + index,
                DueDate = index == 0 ? firstDueDate : GetDueDate(firstDueDate, loan.PaymentFrequency, index),
                PrincipalAmount = installmentPrincipal,
                InterestAmount = installmentInterest,
                PaymentAmount = installmentPrincipal + installmentInterest,
                RemainingBalance = Math.Max(0, Math.Round(principal - principalAllocated, 2)),
                Status = InstallmentStatus.Pending
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

    private static IReadOnlyList<LoanCharge> ApplyLateFees(
        Loan loan,
        DateTime today,
        bool recalculateExistingCharges = false)
    {
        var newCharges = new List<LoanCharge>();

        foreach (var period in LateFeeCalculator.BuildPeriods(loan))
        {
            var calculation = LateFeeCalculator.Calculate(loan, period.Installments, today);
            var existingCharge = loan.Charges.FirstOrDefault(charge =>
                charge.Type == LoanChargeType.LateFee && charge.PeriodNumber == period.Number);
            var requiresFormulaMigration = existingCharge is not null
                && (!existingCharge.Notes?.StartsWith("Mora fija por vencimiento", StringComparison.OrdinalIgnoreCase) ?? true);

            if (calculation.Amount <= 0 || calculation.EligibleThroughDate is null)
            {
                if (existingCharge is not null
                    && (recalculateExistingCharges || requiresFormulaMigration)
                    && existingCharge.AmountPaid < existingCharge.Amount)
                {
                    existingCharge.Amount = existingCharge.AmountPaid;
                    existingCharge.Notes = "Mora recalculada sin saldo vencido en la fecha de pago.";
                    existingCharge.AppliedAtUtc = DateTime.UtcNow;
                }

                continue;
            }

            var eligibleThroughDate = calculation.EligibleThroughDate.Value;
            var lateFeeNotes = $"Mora fija por vencimiento: {calculation.MonthlyRate:0.##}% sobre {calculation.PendingPeriodAmount:N2} que seguían pendientes en sus fechas de pago hasta el {eligibleThroughDate:dd/MM/yyyy}.";
            if (existingCharge is not null)
            {
                var recalculatedAmount = Math.Max(existingCharge.AmountPaid, calculation.Amount);
                if (recalculateExistingCharges
                    || requiresFormulaMigration
                    || existingCharge.Amount != recalculatedAmount
                    || existingCharge.PeriodEndDate.Date != eligibleThroughDate)
                {
                    existingCharge.Amount = recalculatedAmount;
                    existingCharge.PeriodStartDate = period.StartDate;
                    existingCharge.PeriodEndDate = eligibleThroughDate;
                    existingCharge.Notes = lateFeeNotes;
                    existingCharge.AppliedAtUtc = DateTime.UtcNow;
                }

                continue;
            }

            if (calculation.Amount <= 0)
            {
                continue;
            }

            var newCharge = new LoanCharge
            {
                Loan = loan,
                LoanId = loan.Id,
                Type = LoanChargeType.LateFee,
                PeriodNumber = period.Number,
                PeriodStartDate = period.StartDate,
                PeriodEndDate = eligibleThroughDate,
                Amount = calculation.Amount,
                Notes = lateFeeNotes
            };
            loan.Charges.Add(newCharge);
            newCharges.Add(newCharge);
        }

        return newCharges;
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

    private static string NormalizeLateFeePercentage(string? value)
    {
        var normalized = value?.Trim().TrimEnd('%').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "50%";
        }

        var normalizedNumber = normalized.Replace(',', '.');
        if (!decimal.TryParse(normalizedNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage)
            || percentage < 0
            || percentage > 100)
        {
            throw new InvalidOperationException("La tasa de mora debe estar entre 0% y 100%.");
        }

        return $"{percentage:0.##}%";
    }

    private async Task UpsertLateFeeRateChangedNotificationsAsync(
        Loan loan,
        string previousLateFee,
        string currentLateFee,
        CancellationToken cancellationToken)
    {
        var recipients = await dbContext.Users
            .Where(user => user.IsActive
                && (user.Role == UserRole.Admin
                    || user.Role == UserRole.Lender && user.Id == loan.LenderUserId))
            .ToListAsync(cancellationToken);

        var recipientIds = recipients.Select(user => user.Id).ToArray();
        var existingNotifications = await dbContext.Notifications
            .Where(notification => notification.UserId.HasValue
                && recipientIds.Contains(notification.UserId.Value)
                && notification.Type == NotificationType.LateFeeRateChanged
                && notification.RelatedEntityId == loan.Id)
            .ToDictionaryAsync(notification => notification.UserId!.Value, cancellationToken);
        var existingClientNotification = await dbContext.Notifications
            .FirstOrDefaultAsync(notification => notification.ClientId == loan.ClientId
                && notification.Type == NotificationType.LateFeeRateChanged
                && notification.RelatedEntityId == loan.Id, cancellationToken);
        var reference = string.IsNullOrWhiteSpace(loan.ReferenceName)
            ? "este préstamo"
            : $"el préstamo {loan.ReferenceName}";

        foreach (var recipient in recipients)
        {
            var message = $"La tasa de mora de {reference}, correspondiente a {loan.Client.FullName}, cambió de {previousLateFee} a {currentLateFee}.";

            if (existingNotifications.TryGetValue(recipient.Id, out var existing))
            {
                existing.Title = "Tasa de mora actualizada";
                existing.Message = message;
                existing.IsRead = false;
                existing.ReadAtUtc = null;
                existing.CreatedAtUtc = DateTime.UtcNow;
                continue;
            }

            dbContext.Notifications.Add(new Notification
            {
                UserId = recipient.Id,
                Type = NotificationType.LateFeeRateChanged,
                RelatedEntityId = loan.Id,
                Title = "Tasa de mora actualizada",
                Message = message
            });
        }

        var clientMessage = $"La tasa de mora de {reference} cambió de {previousLateFee} a {currentLateFee} de la tasa de interés mensual. Las moras pendientes no pagadas se recalcularon y las futuras usarán la nueva tasa.";
        if (existingClientNotification is not null)
        {
            existingClientNotification.Title = "Tasa de mora actualizada";
            existingClientNotification.Message = clientMessage;
            existingClientNotification.IsRead = false;
            existingClientNotification.ReadAtUtc = null;
            existingClientNotification.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            dbContext.Notifications.Add(new Notification
            {
                ClientId = loan.ClientId,
                Type = NotificationType.LateFeeRateChanged,
                RelatedEntityId = loan.Id,
                Title = "Tasa de mora actualizada",
                Message = clientMessage
            });
        }
    }

    private sealed record LoanRecalculationPlan(
        LoanRecalculationPreviewDto Preview,
        List<Installment> InstallmentsToReplace,
        List<Installment> NewInstallments,
        decimal PaidInterest);
}

internal static class LoanDataOperationLock
{
    public const string ResourceName = "CrediPrest.LoanDataOperation";
    public static readonly SemaphoreSlim Gate = new(1, 1);
}
