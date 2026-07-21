using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrediPrest.Application.Services;

internal sealed class PaymentService(IApplicationDbContext dbContext, ILoanService loanService, ICurrentUserContext currentUser) : IPaymentService
{
    public async Task<LoanDetailDto> RegisterAsync(RegisterPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AmountPaid <= 0)
        {
            throw new InvalidOperationException("El monto pagado debe ser mayor que cero.");
        }

        if (!Enum.IsDefined(request.PaymentMethod))
        {
            throw new InvalidOperationException("Selecciona un método de pago válido.");
        }

        if (request.PaymentDate.Date > BusinessClock.Today)
        {
            throw new InvalidOperationException("La fecha del pago no puede estar en el futuro.");
        }

        if (!request.InstallmentId.HasValue)
        {
            throw new InvalidOperationException("Selecciona la cuota hasta la cual se aplicará el pago.");
        }

        await loanService.RefreshOverdueAsync(cancellationToken);

        Loan loan;
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

            loan = await ApplyOwnershipFilter(dbContext.Loans)
                .Include(item => item.Client)
                .Include(item => item.Installments)
                .Include(item => item.Payments)
                .Include(item => item.Charges)
                .FirstOrDefaultAsync(item => item.Id == request.LoanId && item.Client.IsActive, cancellationToken)
                ?? throw new KeyNotFoundException("Préstamo no encontrado.");

            if (loan.Status == LoanStatus.Cancelled
                && loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid)
                && loan.Charges.All(charge => charge.AmountPaid >= charge.Amount))
            {
                throw new InvalidOperationException("El préstamo ya está cancelado.");
            }

            var receipt = PaymentReceiptFactory.Create(
                request.ReceiptImageBase64,
                request.ReceiptFileName,
                request.ReceiptContentType);
            if (receipt is not null)
            {
                dbContext.PaymentReceipts.Add(receipt);
            }

            var orderedInstallments = GetInstallmentsInPaymentOrder(loan.Installments, request.InstallmentId);
            var paymentDate = request.PaymentDate.Date;
            var payableThroughDate = GetPayableThroughDate(orderedInstallments, request.InstallmentId, paymentDate);
            var payableInstallments = orderedInstallments
                .Where(installment => installment.DueDate.Date <= payableThroughDate)
                .ToList();
            if (payableInstallments.Count == 0 && orderedInstallments.Count > 0)
            {
                // Permite pagar anticipadamente la próxima cuota, pero no distribuir el excedente a más cuotas futuras.
                payableInstallments.Add(orderedInstallments[0]);
            }

            var overdueInstallments = payableInstallments
                .Where(installment => installment.DueDate.Date < paymentDate)
                .ToList();
            var currentInstallments = payableInstallments
                .Where(installment => installment.DueDate.Date >= paymentDate)
                .ToList();
            var pendingCharges = loan.Charges
                .Where(charge => charge.AmountPaid < charge.Amount)
                .OrderBy(charge => charge.PeriodNumber)
                .ToList();

            var registeredPayments = new List<Payment>();
            var remainingPayment = request.AmountPaid;
            remainingPayment = ApplyToInstallments(loan, overdueInstallments, remainingPayment, request, receipt?.Id, registeredPayments);
            remainingPayment = ApplyToCharges(loan, pendingCharges, remainingPayment, request, receipt?.Id, registeredPayments);
            remainingPayment = ApplyToInstallments(loan, currentInstallments, remainingPayment, request, receipt?.Id, registeredPayments);

            if (remainingPayment > 0)
            {
                throw new InvalidOperationException(
                    "El monto supera las cuotas vencidas o actuales para la fecha seleccionada. No se adelantó el excedente a una cuota futura; registra el monto exacto o utiliza Abono extraordinario para reducir capital.");
            }

            if (loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid)
                && loan.Charges.All(charge => charge.AmountPaid >= charge.Amount))
            {
                loan.Status = LoanStatus.Cancelled;
            }

            if (registeredPayments.Count == 0)
            {
                throw new InvalidOperationException("No se pudo aplicar el pago al préstamo seleccionado.");
            }

            await AddPaymentNotificationsAsync(
                loan,
                registeredPayments[0].Id,
                request,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
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

        await loanService.RefreshOverdueAsync(cancellationToken);

        var updatedLoan = await ApplyOwnershipFilter(dbContext.Loans)
            .Include(item => item.Client)
            .Include(item => item.Installments)
            .Include(item => item.Payments)
            .Include(item => item.Charges)
            .FirstAsync(item => item.Id == loan.Id, cancellationToken);

        return updatedLoan.ToDetailDto();
    }

    public async Task<IReadOnlyList<PaymentDto>> ListByLoanAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var paymentsQuery = dbContext.Payments
            .Include(payment => payment.Loan)
            .Include(payment => payment.LoanCharge)
            .Where(payment => payment.LoanId == loanId)
            .Where(payment => payment.Loan.Client.IsActive);

        if (currentUser.IsLender && currentUser.UserId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(payment => payment.Loan.LenderUserId == currentUser.UserId.Value);
        }

        var payments = await paymentsQuery
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return payments.Select(payment => payment.ToDto()).ToList();
    }

    public async Task<PaymentReceiptFileDto> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken = default)
    {
        var paymentsQuery = dbContext.Payments
            .Include(payment => payment.Loan)
            .Where(payment => payment.ReceiptId == receiptId);

        if (currentUser.IsLender && currentUser.UserId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(payment => payment.Loan.LenderUserId == currentUser.UserId.Value);
        }

        var hasAccess = await paymentsQuery.AnyAsync(cancellationToken);
        if (!hasAccess)
        {
            throw new KeyNotFoundException("Comprobante no encontrado.");
        }

        var receipt = await dbContext.PaymentReceipts.FirstOrDefaultAsync(item => item.Id == receiptId, cancellationToken)
            ?? throw new KeyNotFoundException("Comprobante no encontrado.");

        return new PaymentReceiptFileDto(receipt.FileName, receipt.ContentType, receipt.Content);
    }

    private static InstallmentStatus GetInstallmentStatus(Installment installment)
    {
        if (installment.AmountPaid >= installment.PaymentAmount)
        {
            return InstallmentStatus.Paid;
        }

        return installment.DueDate.Date < BusinessClock.Today
            ? InstallmentStatus.Overdue
            : InstallmentStatus.Pending;
    }

    private decimal ApplyToInstallments(
        Loan loan,
        IEnumerable<Installment> installments,
        decimal remainingPayment,
        RegisterPaymentRequest request,
        Guid? receiptId,
        ICollection<Payment> registeredPayments)
    {
        foreach (var installment in installments)
        {
            if (remainingPayment <= 0)
            {
                break;
            }

            var pendingInstallmentAmount = installment.PaymentAmount - installment.AmountPaid;
            if (pendingInstallmentAmount <= 0)
            {
                continue;
            }

            var appliedAmount = Math.Round(Math.Min(remainingPayment, pendingInstallmentAmount), 2);
            installment.AmountPaid = Math.Round(installment.AmountPaid + appliedAmount, 2);
            installment.Status = GetInstallmentStatus(installment);
            installment.PaidAtUtc = installment.Status == InstallmentStatus.Paid ? DateTime.UtcNow : null;

            AddPayment(loan.Id, installment.Id, null, appliedAmount, request, receiptId, registeredPayments);
            remainingPayment = Math.Round(remainingPayment - appliedAmount, 2);
        }

        return remainingPayment;
    }

    private decimal ApplyToCharges(
        Loan loan,
        IEnumerable<LoanCharge> charges,
        decimal remainingPayment,
        RegisterPaymentRequest request,
        Guid? receiptId,
        ICollection<Payment> registeredPayments)
    {
        foreach (var charge in charges)
        {
            if (remainingPayment <= 0)
            {
                break;
            }

            var pendingChargeAmount = charge.Amount - charge.AmountPaid;
            if (pendingChargeAmount <= 0)
            {
                continue;
            }

            var appliedAmount = Math.Round(Math.Min(remainingPayment, pendingChargeAmount), 2);
            charge.AmountPaid = Math.Round(charge.AmountPaid + appliedAmount, 2);
            AddPayment(loan.Id, null, charge.Id, appliedAmount, request, receiptId, registeredPayments);
            remainingPayment = Math.Round(remainingPayment - appliedAmount, 2);
        }

        return remainingPayment;
    }

    private void AddPayment(
        Guid loanId,
        Guid? installmentId,
        Guid? loanChargeId,
        decimal amount,
        RegisterPaymentRequest request,
        Guid? receiptId,
        ICollection<Payment> registeredPayments)
    {
        var payment = new Payment
        {
            LoanId = loanId,
            InstallmentId = installmentId,
            LoanChargeId = loanChargeId,
            PaymentDate = request.PaymentDate.Date,
            AmountPaid = amount,
            PaymentMethod = request.PaymentMethod,
            ReferenceNumber = request.ReferenceNumber?.Trim(),
            Notes = request.Notes?.Trim(),
            ReceiptId = receiptId
        };
        dbContext.Payments.Add(payment);
        registeredPayments.Add(payment);
    }

    private async Task AddPaymentNotificationsAsync(
        Loan loan,
        Guid relatedPaymentId,
        RegisterPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var amount = FormatMoney(request.AmountPaid, loan.Currency);
        var date = request.PaymentDate.ToString("d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-NI"));
        var method = FormatPaymentMethod(request.PaymentMethod);
        var reference = string.IsNullOrWhiteSpace(loan.ReferenceName)
            ? "el préstamo"
            : $"el préstamo {loan.ReferenceName.Trim()}";
        var title = "Pago registrado";
        var staffMessage = $"Se registró un pago de {amount} de {loan.Client.FullName} para {reference}, con fecha {date}, mediante {method}.";
        var clientMessage = $"Registramos tu pago de {amount} para {reference}, con fecha {date}, mediante {method}.";

        var staffUserIds = await dbContext.Users
            .Where(user => user.IsActive
                && (user.Role == UserRole.Admin
                    || (user.Role == UserRole.Lender && user.Id == loan.LenderUserId)))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in staffUserIds)
        {
            dbContext.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.PaymentReceived,
                RelatedEntityId = relatedPaymentId,
                Title = title,
                Message = staffMessage
            });
        }

        dbContext.Notifications.Add(new Notification
        {
            ClientId = loan.ClientId,
            Type = NotificationType.PaymentReceived,
            RelatedEntityId = relatedPaymentId,
            Title = title,
            Message = clientMessage
        });
    }

    private static string FormatMoney(decimal amount, CurrencyType currency)
        => $"{(currency == CurrencyType.Usd ? "USD" : "C$")} {amount:N2}";

    private static string FormatPaymentMethod(PaymentMethod method)
        => method switch
        {
            PaymentMethod.Cash => "efectivo",
            PaymentMethod.Transfer => "transferencia",
            PaymentMethod.Deposit => "depósito",
            PaymentMethod.Kash => "Kash",
            _ => "otro método"
        };

    private static List<Installment> GetInstallmentsInPaymentOrder(IEnumerable<Installment> installments, Guid? selectedInstallmentId)
    {
        var installmentList = installments.ToList();
        var unpaidInstallments = installmentList
            .Where(installment => installment.Status != InstallmentStatus.Paid)
            .ToList();

        if (!selectedInstallmentId.HasValue)
        {
            return unpaidInstallments
                .OrderBy(installment => installment.InstallmentNumber)
                .ToList();
        }

        var selectedInstallment = installmentList.FirstOrDefault(installment => installment.Id == selectedInstallmentId.Value)
            ?? throw new KeyNotFoundException("Cuota seleccionada no encontrada.");

        return unpaidInstallments
            .OrderBy(installment => installment.InstallmentNumber <= selectedInstallment.InstallmentNumber ? 0 : 1)
            .ThenBy(installment => installment.InstallmentNumber)
            .ToList();
    }

    private static DateTime GetPayableThroughDate(
        IReadOnlyCollection<Installment> orderedInstallments,
        Guid? selectedInstallmentId,
        DateTime paymentDate)
    {
        if (!selectedInstallmentId.HasValue)
        {
            return paymentDate;
        }

        var selectedInstallment = orderedInstallments.FirstOrDefault(
            installment => installment.Id == selectedInstallmentId.Value)
            ?? throw new KeyNotFoundException("Cuota seleccionada no encontrada.");
        return selectedInstallment.DueDate.Date > paymentDate
            ? selectedInstallment.DueDate.Date
            : paymentDate;
    }

    private IQueryable<Loan> ApplyOwnershipFilter(IQueryable<Loan> query)
    {
        if (!currentUser.IsLender || !currentUser.UserId.HasValue)
        {
            return query;
        }

        return query.Where(loan => loan.LenderUserId == currentUser.UserId.Value);
    }
}
