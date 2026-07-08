using CrediPrest.Application.Abstractions;
using CrediPrest.Infrastructure.Persistence;
using CrediPrest.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrediPrest.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(options =>
        {
            options.Issuer = configuration["Jwt:Issuer"] ?? options.Issuer;
            options.Audience = configuration["Jwt:Audience"] ?? options.Audience;
            options.SecretKey = configuration["Jwt:SecretKey"] ?? options.SecretKey;
            if (int.TryParse(configuration["Jwt:ExpirationMinutes"], out var expirationMinutes))
            {
                options.ExpirationMinutes = expirationMinutes;
            }
        });

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
