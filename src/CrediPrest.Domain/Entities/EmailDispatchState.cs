namespace CrediPrest.Domain.Entities;

public sealed class EmailDispatchState
{
    public int Id { get; set; } = 1;
    public DateTime ActivatedAtUtc { get; set; } = DateTime.UtcNow;
}
