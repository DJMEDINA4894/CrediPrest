using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class PaymentService(IApplicationDbContext dbContext, ILoanService loanService) : IPaymentService
{
    public async Task<LoanDetailDto> RegisterAsync(RegisterPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AmountPaid <= 0)
        {
            throw new InvalidOperationException("El monto pagado debe ser mayor que cero.");
        }

        var loan = await dbContext.Loans
            .Include(item => item.Client)
            .Include(item => item.Installments)
            .Include(item => item.Payments)
            .FirstOrDefaultAsync(item => item.Id == request.LoanId && item.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Préstamo no encontrado.");

        if (loan.Status == LoanStatus.Cancelled && loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid))
        {
            throw new InvalidOperationException("El préstamo ya está cancelado.");
        }

        var orderedInstallments = loan.Installments
            .Where(installment => installment.Status != InstallmentStatus.Paid)
            .OrderBy(installment => installment.InstallmentNumber)
            .ToList();

        if (request.InstallmentId.HasValue)
        {
            orderedInstallments = orderedInstallments
                .OrderBy(installment => installment.Id == request.InstallmentId.Value ? 0 : 1)
                .ThenBy(installment => installment.InstallmentNumber)
                .ToList();
        }

        var remainingPayment = request.AmountPaid;
        foreach (var installment in orderedInstallments)
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

            dbContext.Payments.Add(new Payment
            {
                LoanId = loan.Id,
                InstallmentId = installment.Id,
                PaymentDate = request.PaymentDate.Date,
                AmountPaid = appliedAmount,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = request.ReferenceNumber?.Trim(),
                Notes = request.Notes?.Trim()
            });

            remainingPayment = Math.Round(remainingPayment - appliedAmount, 2);
        }

        if (remainingPayment > 0)
        {
            throw new InvalidOperationException("El pago excede el saldo pendiente del préstamo.");
        }

        if (loan.Installments.All(installment => installment.Status == InstallmentStatus.Paid))
        {
            loan.Status = LoanStatus.Cancelled;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await loanService.RefreshOverdueAsync(cancellationToken);

        var updatedLoan = await dbContext.Loans
            .Include(item => item.Client)
            .Include(item => item.Installments)
            .Include(item => item.Payments)
            .FirstAsync(item => item.Id == loan.Id, cancellationToken);

        return updatedLoan.ToDetailDto();
    }

    public async Task<IReadOnlyList<PaymentDto>> ListByLoanAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var payments = await dbContext.Payments
            .Include(payment => payment.Loan)
            .Where(payment => payment.LoanId == loanId)
            .Where(payment => payment.Loan.Client.IsActive)
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
}
