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
            AmortizationMethod = AmortizationMethod.DecliningBalance,
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
        await RefreshOverdueAsync(cancellationToken);
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

        await RefreshOverdueAsync(cancellationToken);
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
            var receipt = PaymentReceiptFactory.Create(
                request.ReceiptImageBase64,
                request.ReceiptFileName,
                request.ReceiptContentType);
            if (receipt is not null)
            {
                dbContext.PaymentReceipts.Add(receipt);
            }

            if (request.Mode == LoanRecalculationMode.Payoff)
            {
                ValidateSettlementAmount(request.Amount, plan.Preview.TotalSettlementAmount);
                await ApplyLoanPayoffAsync(loan, plan, request, receipt?.Id, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return (await LoadLoanAsync(id, cancellationToken)).ToDetailDto();
            }

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
                ReceiptId = receipt?.Id,
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
            if (loan.Status == LoanStatus.Cancelled)
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
        var schedule = CalculateInstallmentAmounts(
            loan.PrincipalAmount,
            loan.MonthlyInterestRate,
            installmentCount,
            loan.PaymentFrequency,
            loan.AmortizationMethod);
        loan.TotalInterest = Math.Round(schedule.Sum(item => item.Interest), 2);
        loan.TotalToPay = loan.PrincipalAmount + loan.TotalInterest;
        return schedule
            .Select((item, index) => new Installment
            {
                Loan = loan,
                LoanId = loan.Id,
                InstallmentNumber = index + 1,
                DueDate = GetDueDate(loan.StartDate, loan.PaymentFrequency, index + 1),
                PrincipalAmount = item.Principal,
                InterestAmount = item.Interest,
                PaymentAmount = item.Payment,
                RemainingBalance = item.RemainingBalance
            })
            .ToList();
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

        var orderedInstallments = loan.Installments
            .OrderBy(installment => installment.InstallmentNumber)
            .ToList();
        var pendingInstallments = orderedInstallments
            .Where(installment => installment.AmountPaid < installment.PaymentAmount)
            .ToList();
        var pendingCharges = loan.Charges
            .Where(charge => charge.AmountPaid < charge.Amount)
            .OrderBy(charge => charge.PeriodNumber)
            .ToList();
        if (pendingInstallments.Count == 0 && pendingCharges.Count == 0)
        {
            throw new InvalidOperationException("El préstamo no tiene cuotas pendientes para aplicar el abono.");
        }

        if (mode == LoanRecalculationMode.Payoff)
        {
            return BuildLoanPayoffPlan(
                loan,
                effectiveDate,
                orderedInstallments,
                pendingInstallments,
                pendingCharges);
        }

        if (extraordinaryAmount <= 0)
        {
            throw new InvalidOperationException("El abono extraordinario debe ser mayor que cero.");
        }

        var partiallyPaid = orderedInstallments.Any(installment =>
            installment.AmountPaid > 0 && installment.AmountPaid < installment.PaymentAmount);
        if (partiallyPaid)
        {
            throw new InvalidOperationException("Completa primero la cuota parcialmente pagada antes de realizar un abono extraordinario.");
        }

        if (pendingInstallments.Any(installment => installment.DueDate.Date < BusinessClock.Today))
        {
            throw new InvalidOperationException("Debes cancelar las cuotas vencidas antes de realizar un abono extraordinario.");
        }

        if (pendingCharges.Count > 0)
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
                loan.AmortizationMethod,
                pendingInstallments.Count,
                currentPayment),
            LoanRecalculationMode.CustomTerm => ValidateCustomInstallmentCount(requestedInstallmentCount),
            _ => pendingInstallments.Count
        };
        var newSchedule = CalculateInstallmentAmounts(
            principalAfterPayment,
            loan.MonthlyInterestRate,
            newInstallmentCount,
            loan.PaymentFrequency,
            loan.AmortizationMethod);
        var newInterest = Math.Round(newSchedule.Sum(item => item.Interest), 2);
        var firstDueDate = GetFirstRecalculatedDueDate(
            pendingInstallments.Min(installment => installment.DueDate.Date),
            effectiveDate,
            loan.PaymentFrequency);
        var firstInstallmentNumber = firstPendingInstallmentNumber;
        var newInstallments = BuildInstallments(
            loan,
            newSchedule,
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
            Math.Round(principalAfterPayment + newInterest, 2),
            0,
            0,
            0,
            0);

        return new LoanRecalculationPlan(
            preview,
            pendingInstallments,
            newInstallments,
            paidInstallments.Sum(installment => installment.InterestAmount));
    }

    private static LoanRecalculationPlan BuildLoanPayoffPlan(
        Loan loan,
        DateTime effectiveDate,
        IReadOnlyList<Installment> orderedInstallments,
        List<Installment> pendingInstallments,
        List<LoanCharge> pendingCharges)
    {
        var allocations = pendingInstallments
            .Select(installment =>
            {
                var amountPaid = Math.Max(0, installment.AmountPaid);
                var paidInterest = Math.Min(amountPaid, installment.InterestAmount);
                var paidPrincipal = Math.Min(
                    installment.PrincipalAmount,
                    Math.Max(0, amountPaid - paidInterest));
                return new LoanPayoffAllocation(
                    installment,
                    Math.Round(paidInterest, 2),
                    Math.Round(paidPrincipal, 2),
                    Math.Round(Math.Max(0, installment.PrincipalAmount - paidPrincipal), 2),
                    Math.Round(Math.Max(0, installment.InterestAmount - paidInterest), 2),
                    0);
            })
            .ToList();

        foreach (var allocation in allocations.Where(item => item.Installment.DueDate.Date <= effectiveDate))
        {
            allocation.AccruedInterest = allocation.UnpaidScheduledInterest;
        }

        var firstFutureAllocation = allocations
            .Where(item => item.Installment.DueDate.Date > effectiveDate)
            .OrderBy(item => item.Installment.DueDate)
            .FirstOrDefault();
        if (firstFutureAllocation is not null)
        {
            var previousDueDate = orderedInstallments
                .Where(installment => installment.DueDate.Date < firstFutureAllocation.Installment.DueDate.Date)
                .OrderByDescending(installment => installment.DueDate)
                .Select(installment => installment.DueDate.Date)
                .FirstOrDefault();
            var periodStartDate = previousDueDate == default
                ? loan.StartDate.Date
                : previousDueDate;
            var periodDays = Math.Max(1, (firstFutureAllocation.Installment.DueDate.Date - periodStartDate).Days);
            var elapsedDays = Math.Clamp((effectiveDate - periodStartDate).Days, 0, periodDays);
            var grossAccruedInterest = Math.Round(
                firstFutureAllocation.Installment.InterestAmount * elapsedDays / periodDays,
                2);
            firstFutureAllocation.AccruedInterest = Math.Round(
                Math.Max(0, grossAccruedInterest - firstFutureAllocation.PaidInterest),
                2);
        }

        var outstandingPrincipal = Math.Round(allocations.Sum(item => item.OutstandingPrincipal), 2);
        var currentPendingInterest = Math.Round(allocations.Sum(item => item.UnpaidScheduledInterest), 2);
        var accruedInterest = Math.Round(allocations.Sum(item => item.AccruedInterest), 2);
        var pendingLateFees = Math.Round(
            pendingCharges.Sum(charge => Math.Max(0, charge.Amount - charge.AmountPaid)),
            2);
        var futureInterestDiscount = Math.Round(
            Math.Max(0, currentPendingInterest - accruedInterest),
            2);
        var totalSettlementAmount = Math.Round(
            outstandingPrincipal + accruedInterest + pendingLateFees,
            2);
        if (totalSettlementAmount <= 0)
        {
            throw new InvalidOperationException("El préstamo no tiene saldo pendiente para liquidar.");
        }

        var paidInstallments = orderedInstallments.Count(installment =>
            installment.AmountPaid >= installment.PaymentAmount);
        var currentInstallmentAmount = pendingInstallments
            .OrderBy(installment => installment.InstallmentNumber)
            .Select(installment => installment.PaymentAmount)
            .FirstOrDefault();
        var preview = new LoanRecalculationPreviewDto(
            loan.Id,
            LoanRecalculationMode.Payoff,
            effectiveDate,
            effectiveDate,
            outstandingPrincipal,
            totalSettlementAmount,
            0,
            currentInstallmentAmount,
            0,
            paidInstallments,
            pendingInstallments.Count,
            0,
            currentPendingInterest,
            0,
            futureInterestDiscount,
            0,
            accruedInterest,
            pendingLateFees,
            futureInterestDiscount,
            totalSettlementAmount);

        return new LoanRecalculationPlan(
            preview,
            pendingInstallments,
            [],
            orderedInstallments
                .Where(installment => installment.AmountPaid >= installment.PaymentAmount)
                .Sum(installment => installment.InterestAmount),
            allocations,
            pendingCharges);
    }

    private async Task ApplyLoanPayoffAsync(
        Loan loan,
        LoanRecalculationPlan plan,
        RegisterExtraordinaryPaymentRequest request,
        Guid? receiptId,
        CancellationToken cancellationToken)
    {
        var allocations = plan.PayoffAllocations
            ?? throw new InvalidOperationException("No se pudo calcular la distribución de la liquidación.");
        var charges = plan.ChargesToPay ?? [];
        var relatedEntityIds = allocations
            .Select(item => item.Installment.Id)
            .Concat(charges.Select(charge => charge.Id))
            .Distinct()
            .ToArray();
        if (relatedEntityIds.Length > 0)
        {
            var obsoleteNotifications = await dbContext.Notifications
                .Where(notification => relatedEntityIds.Contains(notification.RelatedEntityId))
                .ToListAsync(cancellationToken);
            dbContext.Notifications.RemoveRange(obsoleteNotifications);
        }

        foreach (var allocation in allocations)
        {
            var installment = allocation.Installment;
            installment.InterestAmount = Math.Round(
                allocation.PaidInterest + allocation.AccruedInterest,
                2);
            installment.PaymentAmount = Math.Round(
                installment.PrincipalAmount + installment.InterestAmount,
                2);
            installment.AmountPaid = installment.PaymentAmount;
            installment.RemainingBalance = 0;
            installment.Status = InstallmentStatus.Paid;
            installment.PaidAtUtc = DateTime.UtcNow;
        }

        foreach (var charge in charges)
        {
            charge.AmountPaid = charge.Amount;
        }

        var preview = plan.Preview;
        var systemNotes =
            $"Liquidación total. Capital pendiente: {preview.OutstandingPrincipal:N2}; " +
            $"interés generado: {preview.AccruedInterest:N2}; mora pendiente: {preview.PendingLateFees:N2}; " +
            $"interés futuro descontado: {preview.FutureInterestDiscount:N2}.";
        var userNotes = NormalizeOptional(request.Notes);
        var payment = new Payment
        {
            Loan = loan,
            LoanId = loan.Id,
            PaymentDate = request.EffectiveDate.Date,
            AmountPaid = preview.TotalSettlementAmount,
            Type = PaymentType.LoanPayoff,
            PaymentMethod = request.PaymentMethod,
            ReferenceNumber = NormalizeOptional(request.ReferenceNumber),
            Notes = userNotes is null ? systemNotes : $"{systemNotes} {userNotes}",
            ReceiptId = receiptId,
            RecalculationMode = LoanRecalculationMode.Payoff,
            PreviousOutstandingPrincipal = preview.OutstandingPrincipal,
            NewOutstandingPrincipal = 0,
            PreviousInstallmentAmount = preview.CurrentInstallmentAmount,
            NewInstallmentAmount = 0,
            PreviousInstallmentCount = preview.CurrentRemainingInstallments,
            NewInstallmentCount = 0,
            PreviousPendingInterest = preview.CurrentPendingInterest,
            NewPendingInterest = 0
        };
        loan.Payments.Add(payment);
        dbContext.Payments.Add(payment);

        loan.TotalInterest = Math.Round(loan.Installments.Sum(installment => installment.InterestAmount), 2);
        loan.TotalToPay = Math.Round(loan.PrincipalAmount + loan.TotalInterest, 2);
        loan.EndDate = request.EffectiveDate.Date;
        loan.Status = LoanStatus.Cancelled;
    }

    private static void ValidateSettlementAmount(decimal receivedAmount, decimal settlementAmount)
    {
        if (receivedAmount <= 0)
        {
            throw new InvalidOperationException("El monto recibido para liquidar debe ser mayor que cero.");
        }

        if (Math.Abs(receivedAmount - settlementAmount) > 0.01m)
        {
            throw new InvalidOperationException(
                $"El monto exacto para liquidar el préstamo es {settlementAmount:N2}. Actualiza la vista previa antes de confirmar.");
        }
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
        AmortizationMethod amortizationMethod,
        int maximumInstallments,
        decimal targetPayment)
    {
        for (var count = 1; count <= maximumInstallments; count++)
        {
            var payment = CalculateInstallmentAmounts(
                principal,
                monthlyInterestRate,
                count,
                frequency,
                amortizationMethod)[0].Payment;
            if (payment <= targetPayment)
            {
                return count;
            }
        }

        return maximumInstallments;
    }

    private static IReadOnlyList<InstallmentAmounts> CalculateInstallmentAmounts(
        decimal principal,
        decimal monthlyInterestRate,
        int installmentCount,
        PaymentFrequency frequency,
        AmortizationMethod amortizationMethod)
        => amortizationMethod == AmortizationMethod.DecliningBalance
            ? CalculateDecliningBalanceAmounts(principal, monthlyInterestRate, installmentCount, frequency)
            : CalculateFlatInterestAmounts(principal, monthlyInterestRate, installmentCount, frequency);

    private static IReadOnlyList<InstallmentAmounts> CalculateFlatInterestAmounts(
        decimal principal,
        decimal monthlyInterestRate,
        int installmentCount,
        PaymentFrequency frequency)
    {
        var totalInterest = Math.Round(
            principal * (monthlyInterestRate / 100m) * GetInterestMonthFactor(installmentCount, frequency),
            2);
        var basePrincipal = Math.Round(principal / installmentCount, 2);
        var baseInterest = Math.Round(totalInterest / installmentCount, 2);
        var amounts = new List<InstallmentAmounts>(installmentCount);
        var principalAllocated = 0m;
        var interestAllocated = 0m;

        for (var index = 0; index < installmentCount; index++)
        {
            var isLast = index == installmentCount - 1;
            var installmentPrincipal = Math.Round(
                isLast ? principal - principalAllocated : basePrincipal,
                2);
            var installmentInterest = Math.Round(
                isLast ? totalInterest - interestAllocated : baseInterest,
                2);
            principalAllocated += installmentPrincipal;
            interestAllocated += installmentInterest;
            amounts.Add(new InstallmentAmounts(
                installmentPrincipal,
                installmentInterest,
                installmentPrincipal + installmentInterest,
                Math.Max(0, Math.Round(principal - principalAllocated, 2))));
        }

        return amounts;
    }

    private static IReadOnlyList<InstallmentAmounts> CalculateDecliningBalanceAmounts(
        decimal principal,
        decimal monthlyInterestRate,
        int installmentCount,
        PaymentFrequency frequency)
    {
        var periodicRate = GetPeriodicInterestRate(monthlyInterestRate, frequency);
        if (periodicRate == 0)
        {
            return CalculateFlatInterestAmounts(principal, 0, installmentCount, frequency);
        }

        var discountFactor = 1m - (decimal)Math.Pow((double)(1m + periodicRate), -installmentCount);
        if (discountFactor <= 0)
        {
            throw new InvalidOperationException("No se pudo calcular la cuota nivelada con la tasa indicada.");
        }

        var levelPayment = Math.Round(principal * periodicRate / discountFactor, 2);
        var remainingPrincipal = principal;
        var amounts = new List<InstallmentAmounts>(installmentCount);

        for (var index = 0; index < installmentCount; index++)
        {
            var isLast = index == installmentCount - 1;
            var installmentInterest = Math.Round(remainingPrincipal * periodicRate, 2);
            var installmentPrincipal = isLast
                ? remainingPrincipal
                : Math.Min(remainingPrincipal, Math.Round(levelPayment - installmentInterest, 2));
            installmentPrincipal = Math.Max(0, installmentPrincipal);
            remainingPrincipal = Math.Max(0, Math.Round(remainingPrincipal - installmentPrincipal, 2));
            amounts.Add(new InstallmentAmounts(
                installmentPrincipal,
                installmentInterest,
                installmentPrincipal + installmentInterest,
                remainingPrincipal));
        }

        return amounts;
    }

    private static decimal GetPeriodicInterestRate(decimal monthlyInterestRate, PaymentFrequency frequency)
        => monthlyInterestRate / 100m / (frequency switch
        {
            PaymentFrequency.Weekly => 4m,
            PaymentFrequency.Biweekly => 2m,
            PaymentFrequency.Monthly => 1m,
            _ => 1m
        });

    private static DateTime GetFirstRecalculatedDueDate(
        DateTime contractualDueDate,
        DateTime effectiveDate,
        PaymentFrequency frequency)
        => contractualDueDate >= effectiveDate
            ? contractualDueDate
            : GetDueDate(effectiveDate, frequency, 1);

    private static List<Installment> BuildInstallments(
        Loan loan,
        IReadOnlyList<InstallmentAmounts> schedule,
        int firstInstallmentNumber,
        DateTime firstDueDate)
    {
        return schedule
            .Select((item, index) => new Installment
            {
                Loan = loan,
                LoanId = loan.Id,
                InstallmentNumber = firstInstallmentNumber + index,
                DueDate = index == 0 ? firstDueDate : GetDueDate(firstDueDate, loan.PaymentFrequency, index),
                PrincipalAmount = item.Principal,
                InterestAmount = item.Interest,
                PaymentAmount = item.Payment,
                RemainingBalance = item.RemainingBalance,
                Status = InstallmentStatus.Pending
            })
            .ToList();
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
                existing.PushVersion++;
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
            existingClientNotification.PushVersion++;
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

    private sealed record InstallmentAmounts(
        decimal Principal,
        decimal Interest,
        decimal Payment,
        decimal RemainingBalance);

    private sealed record LoanRecalculationPlan(
        LoanRecalculationPreviewDto Preview,
        List<Installment> InstallmentsToReplace,
        List<Installment> NewInstallments,
        decimal PaidInterest,
        IReadOnlyList<LoanPayoffAllocation>? PayoffAllocations = null,
        IReadOnlyList<LoanCharge>? ChargesToPay = null);

    private sealed class LoanPayoffAllocation(
        Installment installment,
        decimal paidInterest,
        decimal paidPrincipal,
        decimal outstandingPrincipal,
        decimal unpaidScheduledInterest,
        decimal accruedInterest)
    {
        public Installment Installment { get; } = installment;
        public decimal PaidInterest { get; } = paidInterest;
        public decimal PaidPrincipal { get; } = paidPrincipal;
        public decimal OutstandingPrincipal { get; } = outstandingPrincipal;
        public decimal UnpaidScheduledInterest { get; } = unpaidScheduledInterest;
        public decimal AccruedInterest { get; set; } = accruedInterest;
    }
}

internal static class LoanDataOperationLock
{
    public const string ResourceName = "CrediPrest.LoanDataOperation";
    public static readonly SemaphoreSlim Gate = new(1, 1);
}
