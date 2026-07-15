using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Users;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CrediPrest.Application.Services;

internal sealed class UserService(IApplicationDbContext dbContext, IPasswordHasher passwordHasher, ICurrentUserContext currentUser) : IUserService
{
    private static readonly Regex IdentificationRegex = new(@"^\d{3}-?\d{6}-?\d{4}[A-Za-z]$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"^\+?\d{8,15}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default)
        => await dbContext.Users
            .OrderBy(user => user.Role)
            .ThenBy(user => user.FullName)
            .Select(user => new UserDto(
                user.Id,
                user.ClientId,
                user.UserName,
                user.Email,
                user.FullName,
                user.Phone,
                user.IdentificationNumber,
                user.Role,
                user.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ValidateUser(
            request.UserName,
            request.Email,
            request.FullName,
            request.Phone,
            request.IdentificationNumber,
            request.Password,
            request.ConfirmPassword,
            request.Role,
            request.ClientId,
            isCreate: true);

        var normalizedUserName = request.UserName.Trim().ToLowerInvariant();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var exists = await dbContext.Users.AnyAsync(
            user => user.UserName.ToLower() == normalizedUserName || user.Email.ToLower() == normalizedEmail,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Ya existe un usuario con ese usuario o correo.");
        }

        await EnsureUniqueContactAsync(null, request.Phone, request.IdentificationNumber, cancellationToken);
        await ValidateClientLinkAsync(request.Role, request.ClientId, cancellationToken);

        var user = new User
        {
            ClientId = request.Role == UserRole.Client ? request.ClientId : null,
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            Phone = request.Phone?.Trim(),
            IdentificationNumber = request.IdentificationNumber?.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = request.Role,
            IsActive = request.IsActive
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");
        var requestedUserName = string.IsNullOrWhiteSpace(request.UserName)
            ? user.UserName
            : request.UserName.Trim();

        ValidateUser(
            requestedUserName,
            request.Email,
            request.FullName,
            request.Phone,
            request.IdentificationNumber,
            request.Password,
            request.ConfirmPassword,
            request.Role,
            request.ClientId,
            isCreate: false);

        var normalizedUserName = requestedUserName.ToLowerInvariant();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userOrEmailExists = await dbContext.Users.AnyAsync(
            item => item.Id != id
                && (item.UserName.ToLower() == normalizedUserName || item.Email.ToLower() == normalizedEmail),
            cancellationToken);

        if (userOrEmailExists)
        {
            throw new InvalidOperationException("Ya existe otro usuario con ese nickname o correo.");
        }

        await EnsureUniqueContactAsync(id, request.Phone, request.IdentificationNumber, cancellationToken);
        await ValidateClientLinkAsync(request.Role, request.ClientId, cancellationToken);

        user.ClientId = request.Role == UserRole.Client ? request.ClientId : null;
        user.UserName = requestedUserName;
        user.Email = request.Email.Trim();
        user.FullName = request.FullName.Trim();
        user.Phone = request.Phone?.Trim();
        user.IdentificationNumber = request.IdentificationNumber?.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId == id)
        {
            throw new InvalidOperationException("No puedes eliminar tu propio usuario mientras tienes la sesión activa.");
        }

        var user = await dbContext.Users
            .Include(item => item.Notifications)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        var ownsClients = await dbContext.Clients.AnyAsync(client => client.LenderUserId == id, cancellationToken);
        var ownsLoans = await dbContext.Loans.AnyAsync(loan => loan.LenderUserId == id, cancellationToken);

        if (ownsClients || ownsLoans)
        {
            user.IsActive = false;
        }
        else
        {
            dbContext.Users.Remove(user);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateClientLinkAsync(UserRole role, Guid? clientId, CancellationToken cancellationToken)
    {
        if (role != UserRole.Client)
        {
            return;
        }

        if (!clientId.HasValue)
        {
            throw new InvalidOperationException("Un usuario cliente debe estar vinculado a un cliente.");
        }

        var clientExists = await dbContext.Clients.AnyAsync(client => client.Id == clientId && client.IsActive, cancellationToken);
        if (!clientExists)
        {
            throw new InvalidOperationException("El cliente vinculado no existe o está inactivo.");
        }
    }

    private async Task EnsureUniqueContactAsync(Guid? excludedUserId, string? phone, string? identificationNumber, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var normalizedPhone = NormalizePhone(phone);
            var phoneExists = await dbContext.Users.AnyAsync(
                user => (!excludedUserId.HasValue || user.Id != excludedUserId.Value)
                    && user.Phone != null
                    && user.Phone
                        .Replace(" ", string.Empty)
                        .Replace("-", string.Empty)
                        .Replace("(", string.Empty)
                        .Replace(")", string.Empty) == normalizedPhone,
                cancellationToken);

            if (phoneExists)
            {
                throw new InvalidOperationException("Ya existe un usuario con ese teléfono.");
            }
        }

        if (!string.IsNullOrWhiteSpace(identificationNumber))
        {
            var normalizedIdentification = NormalizeIdentification(identificationNumber);
            var identificationExists = await dbContext.Users.AnyAsync(
                user => (!excludedUserId.HasValue || user.Id != excludedUserId.Value)
                    && user.IdentificationNumber != null
                    && user.IdentificationNumber.Replace("-", string.Empty).ToUpper() == normalizedIdentification,
                cancellationToken);

            if (identificationExists)
            {
                throw new InvalidOperationException("Ya existe un usuario con esa cédula.");
            }
        }
    }

    private static void ValidateUser(
        string userName,
        string email,
        string fullName,
        string? phone,
        string? identificationNumber,
        string? password,
        string? confirmPassword,
        UserRole role,
        Guid? clientId,
        bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException("Usuario, correo y nombre completo son requeridos.");
        }

        if (isCreate && string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("La contraseña es requerida al crear el usuario.");
        }

        if (!string.IsNullOrWhiteSpace(password) && password != confirmPassword)
        {
            throw new InvalidOperationException("La contraseña y la confirmación no coinciden.");
        }

        if (!string.IsNullOrWhiteSpace(password) && password.Length <= 8)
        {
            throw new InvalidOperationException("La contraseña debe tener más de 8 caracteres.");
        }

        if (role == UserRole.Lender)
        {
            if (string.IsNullOrWhiteSpace(phone) || !PhoneRegex.IsMatch(NormalizePhone(phone)))
            {
                throw new InvalidOperationException("El teléfono del prestamista debe tener de 8 a 15 dígitos.");
            }

            if (string.IsNullOrWhiteSpace(identificationNumber) || !IdentificationRegex.IsMatch(identificationNumber.Trim()))
            {
                throw new InvalidOperationException("La cédula del prestamista debe tener el formato 001-010101-0001A o 0010101010001A.");
            }
        }

        if (!Enum.IsDefined(role))
        {
            throw new InvalidOperationException("Rol inválido.");
        }

        if (role == UserRole.Client && !clientId.HasValue)
        {
            throw new InvalidOperationException("Selecciona el cliente vinculado.");
        }
    }

    private static string NormalizePhone(string phone)
        => phone.Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

    private static string NormalizeIdentification(string identificationNumber)
        => identificationNumber.Trim().Replace("-", string.Empty).ToUpperInvariant();

    private static UserDto ToDto(User user)
        => new(
            user.Id,
            user.ClientId,
            user.UserName,
            user.Email,
            user.FullName,
            user.Phone,
            user.IdentificationNumber,
            user.Role,
            user.IsActive);
}
