using CrediPrest.Domain.Entities;

namespace CrediPrest.Application.Abstractions;

public interface IJwtTokenGenerator
{
    string Generate(User user);
}
