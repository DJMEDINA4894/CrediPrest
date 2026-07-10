using CrediPrest.Application.Abstractions;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrediPrest.Infrastructure.Persistence;

public sealed class DatabaseSeeder(AppDbContext dbContext, IPasswordHasher passwordHasher, IConfiguration configuration)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        var adminSeed = configuration.GetSection("AdminSeed");
        var adminUserName = adminSeed["UserName"]?.Trim();
        var adminEmail = adminSeed["Email"]?.Trim();
        var adminFullName = adminSeed["FullName"]?.Trim();
        var adminPassword = adminSeed["Password"];

        if (!string.IsNullOrWhiteSpace(adminUserName))
        {
            var admin = await dbContext.Users.FirstOrDefaultAsync(user => user.UserName == adminUserName, cancellationToken);
            if (admin is not null && admin.Role != UserRole.Admin)
            {
                admin.Role = UserRole.Admin;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (!await dbContext.Users.AnyAsync(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(adminUserName)
                || string.IsNullOrWhiteSpace(adminEmail)
                || string.IsNullOrWhiteSpace(adminPassword))
            {
                return;
            }

            dbContext.Users.Add(new User
            {
                UserName = adminUserName,
                Email = adminEmail,
                FullName = string.IsNullOrWhiteSpace(adminFullName) ? adminUserName : adminFullName,
                Role = UserRole.Admin,
                PasswordHash = passwordHasher.Hash(adminPassword)
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
