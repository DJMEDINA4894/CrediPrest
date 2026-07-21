namespace CrediPrest.Api;

internal sealed class EmailNotificationDispatchHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailNotificationDispatchHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
                await emailService.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falló el envío de notificaciones por correo.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
