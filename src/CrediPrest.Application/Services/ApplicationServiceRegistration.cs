using Microsoft.Extensions.DependencyInjection;

namespace CrediPrest.Application.Services;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IDashboardService, DashboardService>();

        return services;
    }
}
