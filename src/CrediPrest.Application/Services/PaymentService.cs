using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class PaymentService(IApplicationDbContext dbContext, ILoanService loanService, ICurrentUserContext currentUser) : IPaymentService
{
    public async Task<LoanDetailDto> RegisterAsync(RegisterPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AmountPaid <= 0)
        {
            throw new InvalidOperationException("El monto pagado debe ser mayor que cero.");
        }

        await loanService.RefreshOverdueAsync(cancellationToken);

        var loan = await ApplyOwnershipFilter(dbContext.Loans)
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

        var orderedInstallments = GetInstallmentsInPaymentOrder(loan.Installments, request.InstallmentId);
        var today = DateTime.UtcNow.Date;
        var overdueInstallments = orderedInstallments.Where(installment => installment.DueDate.Date < today).ToList();
        var currentInstallments = orderedInstallments.Where(installment => installment.DueDate.Date >= today).ToList();
        var pendingCharges = loan.Charges
            .Where(charge => charge.AmountPaid < charge.Amount)
            .OrderBy(charge => charge.PeriodNumber)
            .ToList();

        var remainingPayment = request.AmountPaid;
        remainingPayment = ApplyToInstallments(loan, overdueInstallments, remainingPayment, request);
        remainingPayment = ApplyToCharges(loan, pendingCharges, remainingPayment, request);
        remainingPayment = ApplyToInstallments(loan, currentInstallments, remainingPayment, request);

        if (remainingPayment > 0)
        {
            throw new InvalidOperationException("El pago excede el saldo pendiente del préstamo.");
        }

        if (loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid)
            && loan.Charges.All(charge => charge.AmountPaid >= charge.Amount))
        {
            loan.Status = LoanStatus.Cancelled;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static InstallmentStatus GetInstallmentStatus(Installment installment)
    {
        if (installment.AmountPaid >= installment.PaymentAmount)
        {
            return InstallmentStatus.Paid;
        }

        return installment.DueDate.Date < DateTime.UtcNow.Date
            ? InstallmentStatus.Overdue
            : InstallmentStatus.Pending;
    }

    private decimal ApplyToInstallments(
        Loan loan,
        IEnumerable<Installment> installments,
        decimal remainingPayment,
        RegisterPaymentRequest request)
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

            AddPayment(loan.Id, installment.Id, null, appliedAmount, request);
            remainingPayment = Math.Round(remainingPayment - appliedAmount, 2);
        }

        return remainingPayment;
    }

    private decimal ApplyToCharges(
        Loan loan,
        IEnumerable<LoanCharge> charges,
        decimal remainingPayment,
        RegisterPaymentRequest request)
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
            AddPayment(loan.Id, null, charge.Id, appliedAmount, request);
            remainingPayment = Math.Round(remainingPayment - appliedAmount, 2);
        }

        return remainingPayment;
    }

    private void AddPayment(Guid loanId, Guid? installmentId, Guid? loanChargeId, decimal amount, RegisterPaymentRequest request)
        => dbContext.Payments.Add(new Payment
        {
            LoanId = loanId,
            InstallmentId = installmentId,
            LoanChargeId = loanChargeId,
            PaymentDate = request.PaymentDate.Date,
            AmountPaid = amount,
            PaymentMethod = request.PaymentMethod,
            ReferenceNumber = request.ReferenceNumber?.Trim(),
            Notes = request.Notes?.Trim()
        });

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

    private IQueryable<Loan> ApplyOwnershipFilter(IQueryable<Loan> query)
    {
        if (!currentUser.IsLender || !currentUser.UserId.HasValue)
        {
            return query;
        }

        return query.Where(loan => loan.LenderUserId == currentUser.UserId.Value);
    }
}
