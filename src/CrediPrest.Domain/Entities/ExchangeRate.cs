namespace CrediPrest.Domain.Entities;

public sealed class ExchangeRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime RateDate { get; set; }
    public decimal CordobasPerUsd { get; set; }
    public decimal BuyCordobasPerUsd { get; set; }
    public decimal SellCordobasPerUsd { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime RetrievedAtUtc { get; set; } = DateTime.UtcNow;
}
