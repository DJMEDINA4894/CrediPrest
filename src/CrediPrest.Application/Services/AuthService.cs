using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Auth;
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
            user.FullName);
    }
}
