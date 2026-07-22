using CrediPrest.Application.DTOs.ExchangeRates;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController(IExchangeRateService exchangeRateService) : ControllerBase
{
    [HttpGet("current")]
    [AllowAnonymous]
    public async Task<ActionResult<ExchangeRateDto>> Current(CancellationToken cancellationToken)
        => Ok(await exchangeRateService.GetAsync(BusinessClock.Today, cancellationToken));
}
