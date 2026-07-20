namespace CrediPrest.Application.Services;

public static class BusinessClock
{
    private static readonly TimeZoneInfo NicaraguaTimeZone = ResolveNicaraguaTimeZone();

    public static DateTime Now
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, NicaraguaTimeZone).DateTime;

    public static DateTime Today => Now.Date;

    private static TimeZoneInfo ResolveNicaraguaTimeZone()
    {
        foreach (var id in new[] { "America/Managua", "Central America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "NicaraguaFallback",
            TimeSpan.FromHours(-6),
            "Nicaragua",
            "Nicaragua");
    }
}
