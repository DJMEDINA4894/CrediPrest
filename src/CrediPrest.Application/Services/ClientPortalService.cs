using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Loans;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class ClientPortalService(IApplicationDbContext dbContext, ILoanService loanService) : IClientPortalService
{
    public async Task<IReadOnlyList<LoanDetailDto>> ListPaymentPlansAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        await loanService.RefreshOverdueAsync(cancellationToken);

        var loans = await dbContext.Loans
            .Include(loan => loan.Client)
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.Installments)
            .Include(loan => loan.Payments)
            .Where(loan => loan.ClientId == clientId && loan.Client.IsActive)
            .OrderByDescending(loan => loan.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return loans.Select(loan => loan.ToDetailDto()).ToList();
    }
}
