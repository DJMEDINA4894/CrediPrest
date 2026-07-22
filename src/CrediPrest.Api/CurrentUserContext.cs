using System.Security.Claims;
using CrediPrest.Application.Abstractions;
using CrediPrest.Domain.Enums;

namespace CrediPrest.Api;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? UserId => TryGetGuid(ClaimTypes.NameIdentifier) ?? TryGetGuid("sub");

    public Guid? ClientId => TryGetGuid("clientId");

    public UserRole? Role
    {
        get
        {
            var role = User?.FindFirstValue(ClaimTypes.Role)
                ?? User?.FindFirstValue("role")
                ?? User?.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
            return Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed) ? parsed : null;
        }
    }

    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsLender => Role == UserRole.Lender;
    public bool IsClient => Role == UserRole.Client;

    private Guid? TryGetGuid(string claimType)
    {
        var value = User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}
