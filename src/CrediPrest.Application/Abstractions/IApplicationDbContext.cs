using CrediPrest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Client> Clients { get; }
    DbSet<Loan> Loans { get; }
    DbSet<LoanCharge> LoanCharges { get; }
    DbSet<Installment> Installments { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<LoanStatusCatalog> LoanStatuses { get; }
    DbSet<PaymentMethodCatalog> PaymentMethods { get; }
    Task<int> DeleteInstallmentsByLoanIdAsync(Guid loanId, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
