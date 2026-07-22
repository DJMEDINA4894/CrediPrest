using System.Globalization;
using System.Xml.Linq;
using CrediPrest.Application.DTOs.ExchangeRates;
using CrediPrest.Application.Services;
using CrediPrest.Domain.Entities;
using CrediPrest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrediPrest.Api;

internal sealed class BacExchangeRateService(
    HttpClient httpClient,
    AppDbContext dbContext,
    IConfiguration configuration,
    ILogger<BacExchangeRateService> logger) : IExchangeRateService
{
    private const string SourceName = "BAC Credomatic Nicaragua";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly decimal fallbackBuyRate = ReadFallbackRate(
        configuration["ExchangeRates:FallbackBuyCordobasPerUsd"], 36.30m);
    private readonly decimal fallbackSellRate = ReadFallbackRate(
        configuration["ExchangeRates:FallbackSellCordobasPerUsd"], 37.14m);

    public async Task<ExchangeRateDto> GetAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var requestedDate = ValidateDate(date);
        var existing = await dbContext.ExchangeRates
            .AsNoTracking()
            .FirstOrDefaultAsync(rate => rate.RateDate == requestedDate, cancellationToken);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        if (requestedDate < BusinessClock.Today)
        {
            var historical = await FindLatestSavedRateAsync(requestedDate, cancellationToken);
            return historical is not null ? ToDto(historical) : CreateFallbackDto(requestedDate);
        }

        try
        {
            return await EnsureCurrentRateAsync(requestedDate, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "No se pudo consultar el tipo de cambio BAC para {RateDate}.", requestedDate);
            var latest = await FindLatestSavedRateAsync(requestedDate, cancellationToken);
            if (latest is not null)
            {
                return ToDto(latest);
            }

            return CreateFallbackDto(requestedDate);
        }
    }

    public async Task<ExchangeRateDto> RefreshAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var requestedDate = ValidateDate(date);
        if (requestedDate != BusinessClock.Today)
        {
            throw new InvalidOperationException("BAC solo publica sus tasas vigentes; no se puede guardar la tasa actual como una tasa histórica.");
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await RefreshCoreAsync(requestedDate, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<ExchangeRateDto> EnsureCurrentRateAsync(DateTime requestedDate, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await dbContext.ExchangeRates
                .AsNoTracking()
                .FirstOrDefaultAsync(rate => rate.RateDate == requestedDate, cancellationToken);
            return existing is not null
                ? ToDto(existing)
                : await RefreshCoreAsync(requestedDate, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<ExchangeRateDto> RefreshCoreAsync(DateTime requestedDate, CancellationToken cancellationToken)
    {
        BacRates rates;
        var source = SourceName;
        try
        {
            rates = await FetchFromBacAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "BAC no respondió; se guardarán las tasas configuradas de respaldo para {RateDate}.", requestedDate);
            rates = new BacRates(fallbackBuyRate, fallbackSellRate);
            source = "BAC Nicaragua - tasas configuradas de respaldo";
        }

        var entity = await dbContext.ExchangeRates
            .FirstOrDefaultAsync(rate => rate.RateDate == requestedDate, cancellationToken);
        if (entity is null)
        {
            entity = new ExchangeRate { RateDate = requestedDate };
            dbContext.ExchangeRates.Add(entity);
        }

        entity.BuyCordobasPerUsd = rates.Buy;
        entity.SellCordobasPerUsd = rates.Sell;
        entity.CordobasPerUsd = Math.Round((rates.Buy + rates.Sell) / 2m, 6);
        entity.Source = source;
        entity.RetrievedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    private async Task<BacRates> FetchFromBacAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            "exchangerate/showXmlExchangeRate.do",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(responseXml);
        var nicaragua = document.Descendants("country")
            .FirstOrDefault(country => string.Equals(
                country.Element("name")?.Value.Trim(),
                "Nicaragua",
                StringComparison.OrdinalIgnoreCase));
        var buy = ParseRate(nicaragua?.Element("buyRateUSD")?.Value);
        var sell = ParseRate(nicaragua?.Element("saleRateUSD")?.Value);
        if (buy <= 0 || sell <= 0 || sell < buy)
        {
            throw new InvalidOperationException("BAC no devolvió tasas de compra y venta válidas para Nicaragua.");
        }

        return new BacRates(buy, sell);
    }

    private Task<ExchangeRate?> FindLatestSavedRateAsync(DateTime requestedDate, CancellationToken cancellationToken)
        => dbContext.ExchangeRates
            .AsNoTracking()
            .Where(rate => rate.RateDate <= requestedDate)
            .OrderByDescending(rate => rate.RateDate)
            .FirstOrDefaultAsync(cancellationToken);

    private ExchangeRateDto CreateFallbackDto(DateTime requestedDate)
        => new(
            requestedDate,
            Math.Round((fallbackBuyRate + fallbackSellRate) / 2m, 6),
            fallbackBuyRate,
            fallbackSellRate,
            "BAC Nicaragua - tasas configuradas de respaldo",
            DateTime.UtcNow);

    private static DateTime ValidateDate(DateTime date)
    {
        var value = date.Date;
        if (value > BusinessClock.Today)
        {
            throw new InvalidOperationException("No se puede consultar un tipo de cambio futuro.");
        }

        return value;
    }

    private static ExchangeRateDto ToDto(ExchangeRate rate)
    {
        var buy = rate.BuyCordobasPerUsd > 0 ? rate.BuyCordobasPerUsd : rate.CordobasPerUsd;
        var sell = rate.SellCordobasPerUsd > 0 ? rate.SellCordobasPerUsd : rate.CordobasPerUsd;
        return new ExchangeRateDto(
            rate.RateDate,
            rate.CordobasPerUsd,
            buy,
            sell,
            rate.Source,
            rate.RetrievedAtUtc);
    }

    private static decimal ParseRate(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static decimal ReadFallbackRate(string? value, decimal defaultValue)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : defaultValue;

    private sealed record BacRates(decimal Buy, decimal Sell);
}
