using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Auth;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Services;

internal sealed class AuthService(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Usuario/correo y contraseña son requeridos.");
        }

        var normalized = request.UserOrEmail.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .FirstOrDefaultAsync(
                item => item.IsActive
                    && (item.UserName.ToLower() == normalized || item.Email.ToLower() == normalized),
                cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        return new LoginResponse(
            jwtTokenGenerator.Generate(user),
            user.Id,
            user.UserName,
            user.Email,
            user.FullName,
            user.Role.ToString(),
            user.ClientId);
    }

    public async Task<LoginResponse> ClientLoginAsync(ClientLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdentificationOrPhone))
        {
            throw new InvalidOperationException("Ingresa tu cédula o teléfono.");
        }

        var normalized = NormalizeIdentifier(request.IdentificationOrPhone);
        var activeClients = await dbContext.Clients
            .Include(item => item.Loans)
            .ThenInclude(loan => loan.Installments)
            .Where(item => item.IsActive)
            .ToListAsync(cancellationToken);
        var client = activeClients.FirstOrDefault(
            item => NormalizeIdentifier(item.IdentificationNumber) == normalized
                || NormalizeIdentifier(item.Phone) == normalized);

        if (client is null)
        {
            throw new UnauthorizedAccessException("No encontramos un cliente activo con esa cédula o teléfono.");
        }

        var hasOpenLoans = client.Loans.Any(loan => loan.Status != LoanStatus.Cancelled && loan.Installments.Any(installment => installment.Status != InstallmentStatus.Paid));
        if (!hasOpenLoans)
        {
            throw new UnauthorizedAccessException("No tienes préstamos pendientes para consultar.");
        }

        return new LoginResponse(
            jwtTokenGenerator.GenerateClient(client),
            client.Id,
            client.IdentificationNumber,
            client.Email ?? string.Empty,
            client.FullName,
            UserRole.Client.ToString(),
            client.Id);
    }

    private static string NormalizeIdentifier(string value)
        => value.Trim()
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .ToUpperInvariant();
}
