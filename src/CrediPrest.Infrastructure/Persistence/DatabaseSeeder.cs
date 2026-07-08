using CrediPrest.Application.Abstractions;
using CrediPrest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Infrastructure.Persistence;

public sealed class DatabaseSeeder(AppDbContext dbContext, IPasswordHasher passwordHasher)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (!await dbContext.Users.AnyAsync(cancellationToken))
        {
            dbContext.Users.Add(new User
            {
                UserName = "admin",
                Email = "admin@crediprest.local",
                FullName = "Administrador",
                PasswordHash = passwordHasher.Hash("Admin123*")
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
