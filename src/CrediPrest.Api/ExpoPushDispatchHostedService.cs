namespace CrediPrest.Api;

internal sealed class ExpoPushDispatchHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpoPushDispatchHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var pushService = scope.ServiceProvider.GetRequiredService<IExpoPushNotificationService>();
                await pushService.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falló el envío de notificaciones push.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
