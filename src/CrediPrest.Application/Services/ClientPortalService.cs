using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class ClientPortalService(IApplicationDbContext dbContext) : IClientPortalService
{
    public async Task<IReadOnlyList<LoanDetailDto>> ListPaymentPlansAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var loans = await dbContext.Loans
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Include(loan => loan.Charges)
            .Where(loan => loan.ClientId == clientId && loan.Client.IsActive)
            .OrderByDescending(loan => loan.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return loans.Select(loan => loan.ToDetailDto()).ToList();
    }

    public async Task<LoanDetailDto> GetPaymentPlanAsync(Guid clientId, Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await dbContext.Loans
            .Include(item => item.Client)
            .Include(item => item.LenderUser)
            .Include(item => item.Installments)
            .Include(item => item.Payments)
            .Include(item => item.Charges)
            .SingleOrDefaultAsync(item => item.Id == loanId && item.ClientId == clientId && item.Client.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("No se encontró el préstamo solicitado.");

        return loan.ToDetailDto();
    }
}
