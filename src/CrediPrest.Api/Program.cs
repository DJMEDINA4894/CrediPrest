using System.Text;
using System.Security.Claims;
using CrediPrest.Api;
using CrediPrest.Application.Services;
using CrediPrest.Domain.Enums;
using CrediPrest.Infrastructure;
using CrediPrest.Infrastructure.Persistence;
using CrediPrest.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CrediPrest.Application.Abstractions.ICurrentUserContext, CurrentUserContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration);
builder.Services.AddHttpClient<IExpoPushNotificationService, ExpoPushNotificationService>(client =>
{
    client.BaseAddress = new Uri("https://exp.host/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IWebPushNotificationService, WebPushNotificationService>();
builder.Services.AddScoped<IEmailNotificationService, AzureEmailNotificationService>();
builder.Services.AddHostedService<AutomaticMaintenanceService>();
builder.Services.AddHostedService<ExpoPushDispatchHostedService>();
builder.Services.AddHostedService<WebPushDispatchHostedService>();
builder.Services.AddHostedService<EmailNotificationDispatchHostedService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var identifier = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Principal?.FindFirstValue("sub");
                if (!Guid.TryParse(identifier, out var subjectId))
                {
                    context.Fail("Usuario no identificado.");
                    return;
                }

                var database = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (context.Principal?.IsInRole(UserRole.Client.ToString()) == true)
                {
                    var isActiveClient = await database.Clients.AnyAsync(
                        client => client.Id == subjectId && client.IsActive,
                        context.HttpContext.RequestAborted);
                    if (!isActiveClient)
                    {
                        context.Fail("El cliente está inactivo o fue eliminado.");
                    }

                    return;
                }

                var user = await database.Users
                    .AsNoTracking()
                    .Where(item => item.Id == subjectId && item.IsActive)
                    .Select(item => new { item.Role })
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                var tokenRole = context.Principal?.FindFirstValue(ClaimTypes.Role)
                    ?? context.Principal?.FindFirstValue("role");

                if (user is null)
                {
                    context.Fail("El usuario está inactivo o fue eliminado.");
                }
                else if (!Enum.TryParse<UserRole>(tokenRole, ignoreCase: true, out var parsedRole)
                    || parsedRole != user.Role)
                {
                    context.Fail("El rol de la sesión cambió. Inicia sesión nuevamente.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BackOffice", policy => policy.RequireRole("Admin", "Lender"));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ClientOnly", policy => policy.RequireRole("Client"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors("Frontend");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "CrediPrestApp" })).AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "CrediPrestApp" })).AllowAnonymous();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

app.Run();
