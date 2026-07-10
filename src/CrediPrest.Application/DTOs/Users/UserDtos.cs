using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.DTOs.Users;

public sealed record UserDto(
    Guid Id,
    Guid? ClientId,
    string UserName,
    string Email,
    string FullName,
    string? Phone,
    string? IdentificationNumber,
    UserRole Role,
    bool IsActive);

public sealed record CreateUserRequest(
    Guid? ClientId,
    string UserName,
    string Email,
    string FullName,
    string? Phone,
    string? IdentificationNumber,
    string Password,
    string? ConfirmPassword,
    UserRole Role,
    bool IsActive);

public sealed record UpdateUserRequest(
    Guid? ClientId,
    string Email,
    string FullName,
    string? Phone,
    string? IdentificationNumber,
    string? Password,
    string? ConfirmPassword,
    UserRole Role,
    bool IsActive);
