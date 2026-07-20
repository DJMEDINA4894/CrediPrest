using CrediPrest.Domain.Enums;

namespace CrediPrest.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? ClientId { get; }
    UserRole? Role { get; }
    bool IsAdmin { get; }
    bool IsLender { get; }
    bool IsClient { get; }
}
