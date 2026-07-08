using CrediPrest.Application.DTOs.Auth;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => Ok(await authService.LoginAsync(request, cancellationToken));
}
