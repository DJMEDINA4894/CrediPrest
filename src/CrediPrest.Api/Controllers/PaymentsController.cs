using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize(Policy = "BackOffice")]
[Route("api/[controller]")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<LoanDetailDto>> Register(RegisterPaymentRequest request, CancellationToken cancellationToken)
        => Ok(await paymentService.RegisterAsync(request, cancellationToken));
}
