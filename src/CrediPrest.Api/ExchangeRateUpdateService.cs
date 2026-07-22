using CrediPrest.Application.Services;

namespace CrediPrest.Api;

internal sealed class ExchangeRateUpdateService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ExchangeRateUpdateService> logger) : BackgroundService
{
    private readonly TimeOnly runTime = ReadRunTime(configuration["ExchangeRates:LocalRunTime"]);
    private readonly TimeZoneInfo timeZone = ResolveTimeZone(configuration["ExchangeRates:TimeZoneId"]);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            await Task.Delay(GetNextRunUtc(nowUtc) - nowUtc, stoppingToken);
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IExchangeRateService>();
            var rate = await service.RefreshAsync(BusinessClock.Today, cancellationToken);
            logger.LogInformation(
                "Tipo de cambio BAC actualizado para {Date}: compra C$ {BuyRate}, venta C$ {SellRate}.",
                rate.RateDate,
                rate.BuyCordobasPerUsd,
                rate.SellCordobasPerUsd);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudo actualizar automáticamente el tipo de cambio de BAC Nicaragua.");
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

        var local = DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }

    private static TimeOnly ReadRunTime(string? value)
        => TimeOnly.TryParse(value, out var parsed) ? parsed : new TimeOnly(5, 0);

    private static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        foreach (var id in new[] { configuredId, "America/Managua", "Central America Standard Time" }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id!);
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
