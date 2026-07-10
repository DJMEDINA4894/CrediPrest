using CrediPrest.Application.Abstractions;
using CrediPrest.Domain.Entities;
using CrediPrest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LoanStatusCatalog> LoanStatuses => Set<LoanStatusCatalog>();
    public DbSet<PaymentMethodCatalog> PaymentMethods => Set<PaymentMethodCatalog>();

    public Task<int> DeleteInstallmentsByLoanIdAsync(Guid loanId, CancellationToken cancellationToken = default)
        => Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.Installments WHERE LoanId = {loanId}",
            cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.UserName).IsUnique();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasOne(user => user.Client)
                .WithMany()
                .HasForeignKey(user => user.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(user => user.UserName).HasMaxLength(80).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(160).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.FullName).HasMaxLength(160).IsRequired();
            entity.Property(user => user.Phone).HasMaxLength(40);
            entity.Property(user => user.IdentificationNumber).HasMaxLength(40);
            entity.Property(user => user.Role).HasDefaultValue(UserRole.Lender);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("Clients");
            entity.HasKey(client => client.Id);
            entity.HasIndex(client => client.IdentificationNumber).IsUnique();
            entity.HasOne(client => client.LenderUser)
                .WithMany()
                .HasForeignKey(client => client.LenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(client => client.FullName).HasMaxLength(180).IsRequired();
            entity.Property(client => client.IdentificationNumber).HasMaxLength(40).IsRequired();
            entity.Property(client => client.Phone).HasMaxLength(40).IsRequired();
            entity.Property(client => client.Address).HasMaxLength(320);
            entity.Property(client => client.Email).HasMaxLength(160);
            entity.Property(client => client.PersonalReference1).HasMaxLength(180);
            entity.Property(client => client.ReferencePhone1).HasMaxLength(40);
            entity.Property(client => client.PersonalReference2).HasMaxLength(180);
            entity.Property(client => client.ReferencePhone2).HasMaxLength(40);
            entity.Property(client => client.BacAccountNumber).HasMaxLength(80);
            entity.Property(client => client.LafiseAccountNumber).HasMaxLength(80);
            entity.Property(client => client.BamproAccountNumber).HasMaxLength(80);
            entity.Property(client => client.PreferredPaymentMethod).HasMaxLength(40).HasDefaultValue("cash").IsRequired();
            entity.Property(client => client.KashAccount).HasMaxLength(80);
            entity.Property(client => client.Notes).HasMaxLength(1200);
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.ToTable("Loans");
            entity.HasKey(loan => loan.Id);
            entity.HasOne(loan => loan.Client)
                .WithMany(client => client.Loans)
                .HasForeignKey(loan => loan.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(loan => loan.LenderUser)
                .WithMany()
                .HasForeignKey(loan => loan.LenderUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(loan => loan.ReferenceName).HasMaxLength(120);
            entity.Property(loan => loan.PrincipalAmount).HasPrecision(18, 2);
            entity.Property(loan => loan.MonthlyInterestRate).HasPrecision(9, 4);
            entity.Property(loan => loan.TotalInterest).HasPrecision(18, 2);
            entity.Property(loan => loan.TotalToPay).HasPrecision(18, 2);
            entity.Property(loan => loan.Notes).HasMaxLength(1200);
        });

        modelBuilder.Entity<Installment>(entity =>
        {
            entity.ToTable("Installments");
            entity.HasKey(installment => installment.Id);
            entity.HasOne(installment => installment.Loan)
                .WithMany(loan => loan.Installments)
                .HasForeignKey(installment => installment.LoanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(installment => new { installment.LoanId, installment.InstallmentNumber }).IsUnique();
            entity.Property(installment => installment.PaymentAmount).HasPrecision(18, 2);
            entity.Property(installment => installment.InterestAmount).HasPrecision(18, 2);
            entity.Property(installment => installment.PrincipalAmount).HasPrecision(18, 2);
            entity.Property(installment => installment.RemainingBalance).HasPrecision(18, 2);
            entity.Property(installment => installment.AmountPaid).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.Id);
            entity.HasOne(payment => payment.Loan)
                .WithMany(loan => loan.Payments)
                .HasForeignKey(payment => payment.LoanId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.Installment)
                .WithMany(installment => installment.Payments)
                .HasForeignKey(payment => payment.InstallmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(payment => payment.AmountPaid).HasPrecision(18, 2);
            entity.Property(payment => payment.ReferenceNumber).HasMaxLength(120);
            entity.Property(payment => payment.Notes).HasMaxLength(1200);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(notification => notification.Id);
            entity.HasOne(notification => notification.User)
                .WithMany(user => user.Notifications)
                .HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(notification => new { notification.UserId, notification.Type, notification.RelatedEntityId }).IsUnique();
            entity.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(600).IsRequired();
        });

        modelBuilder.Entity<LoanStatusCatalog>(entity =>
        {
            entity.ToTable("LoanStatus");
            entity.HasKey(status => status.Id);
            entity.Property(status => status.Name).HasMaxLength(40).IsRequired();
            entity.HasData(
                new LoanStatusCatalog { Id = (int)LoanStatus.Active, Name = "Activo" },
                new LoanStatusCatalog { Id = (int)LoanStatus.Cancelled, Name = "Cancelado" },
                new LoanStatusCatalog { Id = (int)LoanStatus.Overdue, Name = "Vencido" });
        });

        modelBuilder.Entity<PaymentMethodCatalog>(entity =>
        {
            entity.ToTable("PaymentMethods");
            entity.HasKey(method => method.Id);
            entity.Property(method => method.Name).HasMaxLength(40).IsRequired();
            entity.HasData(
                new PaymentMethodCatalog { Id = (int)PaymentMethod.Cash, Name = "Efectivo" },
                new PaymentMethodCatalog { Id = (int)PaymentMethod.Transfer, Name = "Transferencia" },
                new PaymentMethodCatalog { Id = (int)PaymentMethod.Deposit, Name = "Deposito" },
                new PaymentMethodCatalog { Id = (int)PaymentMethod.Other, Name = "Otro" });
        });
    }
}
