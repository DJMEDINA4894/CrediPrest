using CrediPrest.Application.DTOs.Auth;
using CrediPrest.Application.DTOs.Clients;
using CrediPrest.Application.DTOs.Dashboard;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;

namespace CrediPrest.Application.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}

public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> SearchAsync(string? search, CancellationToken cancellationToken = default);
    Task<ClientDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default);
    Task<ClientDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken = default);
    Task<ClientDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ILoanService
{
    Task<IReadOnlyList<LoanDto>> ListAsync(string? status, CancellationToken cancellationToken = default);
    Task<LoanDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LoanDetailDto> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken = default);
    Task<LoanDetailDto> UpdateAsync(Guid id, UpdateLoanRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid id, CancellationToken cancellationToken = default);
    Task RefreshOverdueAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<LoanDetailDto> RegisterAsync(RegisterPaymentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentDto>> ListByLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}
