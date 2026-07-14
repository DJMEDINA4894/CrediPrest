using System.Security.Claims;
using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClientOnly")]
[Route("api/client-portal")]
public sealed class ClientPortalController(IClientPortalService clientPortalService) : ControllerBase
{
    [HttpGet("payment-plans")]
    public async Task<ActionResult<IReadOnlyList<LoanDetailDto>>> PaymentPlans(CancellationToken cancellationToken)
        => Ok(await clientPortalService.ListPaymentPlansAsync(GetClientId(), cancellationToken));

    [HttpGet("payment-plans/{loanId:guid}/pdf")]
    public async Task<IActionResult> PaymentPlanPdf(Guid loanId, CancellationToken cancellationToken)
    {
        var detail = await clientPortalService.GetPaymentPlanAsync(GetClientId(), loanId, cancellationToken);
        return File(
            LoanPaymentTablePdfBuilder.Build(detail),
            "application/pdf",
            LoanPaymentTablePdfBuilder.FileName(detail.Loan));
    }

    private Guid GetClientId()
    {
        var clientId = User.FindFirstValue("clientId")
            ?? throw new UnauthorizedAccessException("Este usuario no está vinculado a un cliente.");

        return Guid.Parse(clientId);
    }
}
