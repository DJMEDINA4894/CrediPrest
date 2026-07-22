namespace CrediPrest.Application.DTOs.ExchangeRates;

public sealed record ExchangeRateDto(
    DateTime RateDate,
    decimal CordobasPerUsd,
    decimal BuyCordobasPerUsd,
    decimal SellCordobasPerUsd,
    string Source,
    DateTime RetrievedAtUtc);
