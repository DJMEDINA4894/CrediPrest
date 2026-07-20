namespace CrediPrest.Application.DTOs.Auth;

public sealed record LoginRequest(string UserOrEmail, string Password);

public sealed record ClientLoginRequest(string IdentificationOrPhone);

public sealed record LoginResponse(
    string Token,
    Guid UserId,
    string UserName,
    string Email,
    string FullName,
    string Role,
    Guid? ClientId);
