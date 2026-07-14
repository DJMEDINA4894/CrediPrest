using CrediPrest.Application.Services;

namespace CrediPrest.Api;

internal sealed class AutomaticMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AutomaticMaintenanceService> logger) : BackgroundService
{
    private readonly TimeOnly runTime = ReadRunTime(configuration["AutomaticMaintenance:LocalRunTime"]);
    private readonly TimeZoneInfo timeZone = ResolveTimeZone(configuration["AutomaticMaintenance:TimeZoneId"]);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recupera la ejecución nocturna si Azure suspendió o reinició la aplicación.
        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var nextRunUtc = GetNextRunUtc(nowUtc);
            var delay = nextRunUtc - nowUtc;

            logger.LogInformation(
                "Próxima revisión automática de cuotas, moras y notificaciones: {NextRunLocal} ({TimeZoneId}).",
                TimeZoneInfo.ConvertTime(nextRunUtc, timeZone),
                timeZone.Id);

            await Task.Delay(delay, stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
    }

    private DateTimeOffset GetNextRunUtc(DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var nextLocal = localNow.Date.Add(runTime.ToTimeSpan());
        if (nextLocal <= localNow.DateTime)
        {
            nextLocal = nextLocal.AddDays(1);
        }

        var unspecifiedLocal = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, timeZone), TimeSpan.Zero);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notificationService.RefreshAutomaticAsync(cancellationToken);
            logger.LogInformation("Revisión automática de cuotas, moras y notificaciones completada.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló la revisión automática de cuotas, moras y notificaciones.");
        }
    }

    private static TimeOnly ReadRunTime(string? configuredValue)
        => TimeOnly.TryParse(configuredValue, out var configuredTime)
            ? configuredTime
            : new TimeOnly(2, 0);

    private static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        var candidates = new[]
        {
            configuredId,
            "America/Managua",
            "Central America Standard Time"
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate!);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
