namespace CrediPrest.Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "CrediPrestApp";
    public string Audience { get; set; } = "CrediPrestApp";
    public string SecretKey { get; set; } = "CHANGE_ME_CrediPrestApp_Private_Development_Key_32Chars";
    public int ExpirationMinutes { get; set; } = 720;
}
