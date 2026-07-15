using CrediPrest.Application.DTOs.Auth;
using CrediPrest.Application.DTOs.Clients;
using CrediPrest.Application.DTOs.Dashboard;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Notifications;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Application.DTOs.Users;

namespace CrediPrest.Application.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> ClientLoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default);
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
    Task<LoanRecalculationPreviewDto> PreviewExtraordinaryPaymentAsync(Guid id, ExtraordinaryPaymentPreviewRequest request, CancellationToken cancellationToken = default);
    Task<LoanDetailDto> RegisterExtraordinaryPaymentAsync(Guid id, RegisterExtraordinaryPaymentRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task RefreshOverdueAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<LoanDetailDto> RegisterAsync(RegisterPaymentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentDto>> ListByLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
    Task<PaymentReceiptFileDto> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, Guid? clientId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Guid userId, Guid? clientId, Guid notificationId, CancellationToken cancellationToken = default);
    Task RefreshAutomaticAsync(CancellationToken cancellationToken = default);
}

public interface IClientPortalService
{
    Task<IReadOnlyList<LoanDetailDto>> ListPaymentPlansAsync(Guid clientId, CancellationToken cancellationToken = default);
    Task<LoanDetailDto> GetPaymentPlanAsync(Guid clientId, Guid loanId, CancellationToken cancellationToken = default);
}

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
