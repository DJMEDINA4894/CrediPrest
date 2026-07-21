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
    public DbSet<LoanCharge> LoanCharges => Set<LoanCharge>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ExpoPushDevice> ExpoPushDevices => Set<ExpoPushDevice>();
    public DbSet<ExpoPushDelivery> ExpoPushDeliveries => Set<ExpoPushDelivery>();
    public DbSet<WebPushDevice> WebPushDevices => Set<WebPushDevice>();
    public DbSet<WebPushDelivery> WebPushDeliveries => Set<WebPushDelivery>();
    public DbSet<EmailDispatchState> EmailDispatchStates => Set<EmailDispatchState>();
    public DbSet<EmailNotificationDelivery> EmailNotificationDeliveries => Set<EmailNotificationDelivery>();
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
            entity.Property(loan => loan.AmortizationMethod).HasDefaultValue(AmortizationMethod.FlatInterest);
            entity.Property(loan => loan.TotalInterest).HasPrecision(18, 2);
            entity.Property(loan => loan.TotalToPay).HasPrecision(18, 2);
            entity.Property(loan => loan.Notes).HasMaxLength(1200);
            entity.Property(loan => loan.AgreementCity).HasMaxLength(120);
            entity.Property(loan => loan.LateFeeDescription).HasMaxLength(220);
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

        modelBuilder.Entity<LoanCharge>(entity =>
        {
            entity.ToTable("LoanCharges");
            entity.HasKey(charge => charge.Id);
            entity.HasOne(charge => charge.Loan)
                .WithMany(loan => loan.Charges)
                .HasForeignKey(charge => charge.LoanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(charge => new { charge.LoanId, charge.Type, charge.PeriodNumber }).IsUnique();
            entity.Property(charge => charge.Amount).HasPrecision(18, 2);
            entity.Property(charge => charge.AmountPaid).HasPrecision(18, 2);
            entity.Property(charge => charge.Notes).HasMaxLength(320);
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
            entity.HasOne(payment => payment.LoanCharge)
                .WithMany(charge => charge.Payments)
                .HasForeignKey(payment => payment.LoanChargeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.Receipt)
                .WithMany(receipt => receipt.Payments)
                .HasForeignKey(payment => payment.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(payment => payment.AmountPaid).HasPrecision(18, 2);
            entity.Property(payment => payment.Type).HasDefaultValue(PaymentType.Regular);
            entity.Property(payment => payment.PreviousOutstandingPrincipal).HasPrecision(18, 2);
            entity.Property(payment => payment.NewOutstandingPrincipal).HasPrecision(18, 2);
            entity.Property(payment => payment.PreviousInstallmentAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.NewInstallmentAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.PreviousPendingInterest).HasPrecision(18, 2);
            entity.Property(payment => payment.NewPendingInterest).HasPrecision(18, 2);
            entity.Property(payment => payment.ReferenceNumber).HasMaxLength(120);
            entity.Property(payment => payment.Notes).HasMaxLength(1200);
        });

        modelBuilder.Entity<PaymentReceipt>(entity =>
        {
            entity.ToTable("PaymentReceipts");
            entity.HasKey(receipt => receipt.Id);
            entity.Property(receipt => receipt.FileName).HasMaxLength(180).IsRequired();
            entity.Property(receipt => receipt.ContentType).HasMaxLength(80).IsRequired();
            entity.Property(receipt => receipt.Content).IsRequired();
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(notification => notification.Id);
            entity.HasOne(notification => notification.User)
                .WithMany(user => user.Notifications)
                .HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(notification => notification.Client)
                .WithMany(client => client.Notifications)
                .HasForeignKey(notification => notification.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(notification => new { notification.UserId, notification.Type, notification.RelatedEntityId }).IsUnique();
            entity.HasIndex(notification => new { notification.ClientId, notification.Type, notification.RelatedEntityId }).IsUnique();
            entity.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(600).IsRequired();
            entity.Property(notification => notification.PushVersion).HasDefaultValue(1);
        });

        modelBuilder.Entity<ExpoPushDevice>(entity =>
        {
            entity.ToTable("ExpoPushDevices");
            entity.HasKey(device => device.Id);
            entity.HasIndex(device => device.ExpoPushToken).IsUnique();
            entity.HasOne(device => device.User)
                .WithMany()
                .HasForeignKey(device => device.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(device => device.Client)
                .WithMany()
                .HasForeignKey(device => device.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(device => device.ExpoPushToken).HasMaxLength(220).IsRequired();
            entity.Property(device => device.Platform).HasMaxLength(20).IsRequired();
            entity.Property(device => device.DeviceName).HasMaxLength(120);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ExpoPushDevices_Recipient",
                "([UserId] IS NOT NULL AND [ClientId] IS NULL) OR ([UserId] IS NULL AND [ClientId] IS NOT NULL)"));
        });

        modelBuilder.Entity<ExpoPushDelivery>(entity =>
        {
            entity.ToTable("ExpoPushDeliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => new
            {
                delivery.NotificationId,
                delivery.ExpoPushDeviceId,
                delivery.NotificationVersion
            }).IsUnique();
            entity.Property(delivery => delivery.ExpoTicketId).HasMaxLength(200);
            entity.Property(delivery => delivery.ErrorCode).HasMaxLength(80);
            entity.Property(delivery => delivery.ErrorMessage).HasMaxLength(600);
        });

        modelBuilder.Entity<WebPushDevice>(entity =>
        {
            entity.ToTable("WebPushDevices");
            entity.HasKey(device => device.Id);
            entity.HasIndex(device => device.EndpointHash).IsUnique();
            entity.HasOne(device => device.User)
                .WithMany()
                .HasForeignKey(device => device.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(device => device.Client)
                .WithMany()
                .HasForeignKey(device => device.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(device => device.EndpointHash).HasMaxLength(64).IsRequired();
            entity.Property(device => device.Endpoint).HasMaxLength(2048).IsRequired();
            entity.Property(device => device.P256dh).HasMaxLength(512).IsRequired();
            entity.Property(device => device.Auth).HasMaxLength(256).IsRequired();
            entity.Property(device => device.UserAgent).HasMaxLength(300);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_WebPushDevices_Recipient",
                "([UserId] IS NOT NULL AND [ClientId] IS NULL) OR ([UserId] IS NULL AND [ClientId] IS NOT NULL)"));
        });

        modelBuilder.Entity<WebPushDelivery>(entity =>
        {
            entity.ToTable("WebPushDeliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => new
            {
                delivery.NotificationId,
                delivery.WebPushDeviceId,
                delivery.NotificationVersion
            }).IsUnique();
            entity.Property(delivery => delivery.ErrorCode).HasMaxLength(80);
            entity.Property(delivery => delivery.ErrorMessage).HasMaxLength(600);
        });

        modelBuilder.Entity<EmailDispatchState>(entity =>
        {
            entity.ToTable("EmailDispatchState");
            entity.HasKey(state => state.Id);
            entity.Property(state => state.Id).ValueGeneratedNever();
            entity.ToTable(table => table.HasCheckConstraint("CK_EmailDispatchState_Singleton", "[Id] = 1"));
        });

        modelBuilder.Entity<EmailNotificationDelivery>(entity =>
        {
            entity.ToTable("EmailNotificationDeliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => new
            {
                delivery.NotificationId,
                delivery.NotificationVersion,
                delivery.RecipientEmail
            }).IsUnique();
            entity.Property(delivery => delivery.RecipientEmail).HasMaxLength(160).IsRequired();
            entity.Property(delivery => delivery.RecipientName).HasMaxLength(180).IsRequired();
            entity.Property(delivery => delivery.Status).HasDefaultValue(EmailDeliveryStatus.Pending);
            entity.Property(delivery => delivery.ProviderMessageId).HasMaxLength(200);
            entity.Property(delivery => delivery.ErrorCode).HasMaxLength(80);
            entity.Property(delivery => delivery.ErrorMessage).HasMaxLength(600);
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
                new PaymentMethodCatalog { Id = (int)PaymentMethod.Other, Name = "Otro" },
                new PaymentMethodCatalog { Id = (int)PaymentMethod.Kash, Name = "Kash" });
        });
    }
}
