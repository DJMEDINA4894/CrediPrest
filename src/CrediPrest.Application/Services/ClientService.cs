using CrediPrest.Application.Abstractions;
using CrediPrest.Application.DTOs.Clients;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace CrediPrest.Application.Services;

internal sealed class ClientService(IApplicationDbContext dbContext) : IClientService
{
    private static readonly Regex IdentificationRegex = new(@"^\d{3}-?\d{6}-?\d{4}[A-Za-z]$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"^\+?\d{8,15}$", RegexOptions.Compiled);
    private static readonly Regex BankAccountRegex = new(@"^\d{6,24}$", RegexOptions.Compiled);
    private static readonly Regex KashRegex = new(@"^[A-Za-z0-9@._+\-\s]{3,80}$", RegexOptions.Compiled);
    private static readonly HashSet<string> PaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash",
        "bac",
        "lafise",
        "bampro",
        "kash"
    };

    public async Task<IReadOnlyList<ClientDto>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Clients
            .Include(client => client.Loans)
            .ThenInclude(loan => loan.Payments)
            .Include(client => client.Loans)
            .ThenInclude(loan => loan.Installments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(client =>
                client.FullName.ToLower().Contains(term)
                || client.IdentificationNumber.ToLower().Contains(term)
                || client.Phone.ToLower().Contains(term)
                || (client.BacAccountNumber != null && client.BacAccountNumber.ToLower().Contains(term))
                || (client.LafiseAccountNumber != null && client.LafiseAccountNumber.ToLower().Contains(term))
                || (client.BamproAccountNumber != null && client.BamproAccountNumber.ToLower().Contains(term))
                || (client.KashAccount != null && client.KashAccount.ToLower().Contains(term)));
        }

        var clients = await query
            .OrderBy(client => client.FullName)
            .ToListAsync(cancellationToken);

        return clients.Select(client => client.ToDto()).ToList();
    }

    public async Task<ClientDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await LoadClientAsync(id, cancellationToken);
        return client.ToDto();
    }

    public async Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await EnsureIdentificationIsUniqueAsync(request.IdentificationNumber, excludedClientId: null, cancellationToken);

        var client = new Domain.Entities.Client
        {
            FullName = request.FullName.Trim(),
            IdentificationNumber = request.IdentificationNumber.Trim(),
            Phone = request.Phone.Trim(),
            Address = request.Address.Trim(),
            Email = request.Email?.Trim(),
            PersonalReference1 = request.PersonalReference1?.Trim(),
            ReferencePhone1 = request.ReferencePhone1?.Trim(),
            PersonalReference2 = request.PersonalReference2?.Trim(),
            ReferencePhone2 = request.ReferencePhone2?.Trim(),
            BacAccountNumber = request.BacAccountNumber?.Trim(),
            LafiseAccountNumber = request.LafiseAccountNumber?.Trim(),
            BamproAccountNumber = request.BamproAccountNumber?.Trim(),
            PreferredPaymentMethod = NormalizePaymentMethod(request.PreferredPaymentMethod),
            HasKash = request.HasKash || !string.IsNullOrWhiteSpace(request.KashAccount),
            KashAccount = request.KashAccount?.Trim(),
            Notes = request.Notes?.Trim()
        };

        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync(cancellationToken);

        return client.ToDto();
    }

    public async Task<ClientDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await EnsureIdentificationIsUniqueAsync(request.IdentificationNumber, excludedClientId: id, cancellationToken);

        var client = await LoadClientAsync(id, cancellationToken);
        client.FullName = request.FullName.Trim();
        client.IdentificationNumber = request.IdentificationNumber.Trim();
        client.Phone = request.Phone.Trim();
        client.Address = request.Address.Trim();
        client.Email = request.Email?.Trim();
        client.PersonalReference1 = request.PersonalReference1?.Trim();
        client.ReferencePhone1 = request.ReferencePhone1?.Trim();
        client.PersonalReference2 = request.PersonalReference2?.Trim();
        client.ReferencePhone2 = request.ReferencePhone2?.Trim();
        client.BacAccountNumber = request.BacAccountNumber?.Trim();
        client.LafiseAccountNumber = request.LafiseAccountNumber?.Trim();
        client.BamproAccountNumber = request.BamproAccountNumber?.Trim();
        client.PreferredPaymentMethod = NormalizePaymentMethod(request.PreferredPaymentMethod);
        client.HasKash = request.HasKash || !string.IsNullOrWhiteSpace(request.KashAccount);
        client.KashAccount = request.KashAccount?.Trim();
        client.Notes = request.Notes?.Trim();
        client.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(cancellationToken);
        return client.ToDto();
    }

    public async Task<ClientDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var client = await LoadClientAsync(id, cancellationToken);
        client.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        return client.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var client = await dbContext.Clients
            .FirstOrDefaultAsync(client => client.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Cliente no encontrado.");

        var loanIds = await dbContext.Loans
            .Where(loan => loan.ClientId == id)
            .Select(loan => loan.Id)
            .ToListAsync(cancellationToken);

        if (loanIds.Count > 0)
        {
            var installmentIds = await dbContext.Installments
                .Where(installment => loanIds.Contains(installment.LoanId))
                .Select(installment => installment.Id)
                .ToListAsync(cancellationToken);

            var payments = await dbContext.Payments
                .Where(payment => loanIds.Contains(payment.LoanId) || installmentIds.Contains(payment.InstallmentId))
                .ToListAsync(cancellationToken);
            dbContext.Payments.RemoveRange(payments);

            var installments = await dbContext.Installments
                .Where(installment => loanIds.Contains(installment.LoanId))
                .ToListAsync(cancellationToken);
            dbContext.Installments.RemoveRange(installments);

            var loans = await dbContext.Loans
                .Where(loan => loan.ClientId == id)
                .ToListAsync(cancellationToken);
            dbContext.Loans.RemoveRange(loans);
        }

        dbContext.Clients.Remove(client);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Domain.Entities.Client> LoadClientAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Clients
            .Include(client => client.Loans)
            .ThenInclude(loan => loan.Payments)
            .Include(client => client.Loans)
            .ThenInclude(loan => loan.Installments)
            .FirstOrDefaultAsync(client => client.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Cliente no encontrado.");

    private async Task EnsureIdentificationIsUniqueAsync(string identificationNumber, Guid? excludedClientId, CancellationToken cancellationToken)
    {
        var normalizedIdentification = NormalizeIdentification(identificationNumber);
        var exists = await dbContext.Clients.AnyAsync(
            client => (!excludedClientId.HasValue || client.Id != excludedClientId.Value)
                && client.IdentificationNumber.Replace("-", string.Empty).ToUpper() == normalizedIdentification,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Ya existe un cliente registrado con esa cédula.");
        }
    }

    private static void Validate(CreateClientRequest request)
    {
        ValidateCommon(
            request.FullName,
            request.IdentificationNumber,
            request.Phone,
            request.Address,
            request.Email,
            request.ReferencePhone1,
            request.ReferencePhone2,
            request.BacAccountNumber,
            request.LafiseAccountNumber,
            request.BamproAccountNumber,
            request.PreferredPaymentMethod,
            request.KashAccount);
    }

    private static void Validate(UpdateClientRequest request)
    {
        ValidateCommon(
            request.FullName,
            request.IdentificationNumber,
            request.Phone,
            request.Address,
            request.Email,
            request.ReferencePhone1,
            request.ReferencePhone2,
            request.BacAccountNumber,
            request.LafiseAccountNumber,
            request.BamproAccountNumber,
            request.PreferredPaymentMethod,
            request.KashAccount);
    }

    private static void ValidateCommon(
        string fullName,
        string identificationNumber,
        string phone,
        string address,
        string? email,
        string? referencePhone1,
        string? referencePhone2,
        string? bacAccountNumber,
        string? lafiseAccountNumber,
        string? bamproAccountNumber,
        string? preferredPaymentMethod,
        string? kashAccount)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3 || fullName.Trim().Length > 180)
        {
            throw new InvalidOperationException("El nombre del cliente debe tener entre 3 y 180 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(identificationNumber) || !IdentificationRegex.IsMatch(identificationNumber.Trim()))
        {
            throw new InvalidOperationException("La cédula debe tener el formato 001-010101-0001A o 0010101010001A.");
        }

        if (!IsValidPhone(phone))
        {
            throw new InvalidOperationException("El teléfono debe tener de 8 a 15 dígitos. Puede incluir +, espacios o guiones.");
        }

        if (string.IsNullOrWhiteSpace(address) || address.Trim().Length > 320)
        {
            throw new InvalidOperationException("La dirección es requerida y no debe exceder 320 caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email))
        {
            throw new InvalidOperationException("El correo no tiene un formato válido.");
        }

        ValidateOptionalPhone(referencePhone1, "El teléfono de referencia 1");
        ValidateOptionalPhone(referencePhone2, "El teléfono de referencia 2");
        ValidateOptionalBankAccount(bacAccountNumber, "La cuenta BAC");
        ValidateOptionalBankAccount(lafiseAccountNumber, "La cuenta Lafise");
        ValidateOptionalBankAccount(bamproAccountNumber, "La cuenta Bampro");

        if (!PaymentMethods.Contains(NormalizePaymentMethod(preferredPaymentMethod)))
        {
            throw new InvalidOperationException("La forma de pago preferida no es válida.");
        }

        if (!string.IsNullOrWhiteSpace(kashAccount) && !KashRegex.IsMatch(kashAccount.Trim()))
        {
            throw new InvalidOperationException("Kash debe tener de 3 a 80 caracteres y solo puede incluir letras, números, espacios, @, punto, guion, + o _.");
        }
    }

    private static void ValidateOptionalPhone(string? phone, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(phone) && !IsValidPhone(phone))
        {
            throw new InvalidOperationException($"{fieldName} debe tener de 8 a 15 dígitos. Puede incluir +, espacios o guiones.");
        }
    }

    private static void ValidateOptionalBankAccount(string? accountNumber, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(accountNumber) && !BankAccountRegex.IsMatch(accountNumber.Trim()))
        {
            throw new InvalidOperationException($"{fieldName} debe contener solo números y tener entre 6 y 24 dígitos.");
        }
    }

    private static bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var compact = phone.Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        return PhoneRegex.IsMatch(compact);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email.Trim());
            return address.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeIdentification(string identificationNumber)
        => identificationNumber.Trim().Replace("-", string.Empty).ToUpperInvariant();

    private static string NormalizePaymentMethod(string? paymentMethod)
        => string.IsNullOrWhiteSpace(paymentMethod)
            ? "cash"
            : paymentMethod.Trim().ToLowerInvariant();
}
