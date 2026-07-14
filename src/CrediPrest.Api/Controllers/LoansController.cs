using CrediPrest.Application.DTOs.Loans;
using CrediPrest.Application.DTOs.Payments;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize(Policy = "BackOffice")]
[Route("api/[controller]")]
public sealed class LoansController(
    ILoanService loanService,
    IPaymentService paymentService,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LoanDto>>> List([FromQuery] string? status, CancellationToken cancellationToken)
        => Ok(await loanService.ListAsync(status, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LoanDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await loanService.GetDetailAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<LoanDetailDto>> Create(CreateLoanRequest request, CancellationToken cancellationToken)
    {
        var loan = await loanService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = loan.Loan.Id }, loan);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LoanDetailDto>> Update(Guid id, UpdateLoanRequest request, CancellationToken cancellationToken)
        => Ok(await loanService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/recalculation/preview")]
    public async Task<ActionResult<LoanRecalculationPreviewDto>> PreviewRecalculation(
        Guid id,
        RecalculateLoanRequest request,
        CancellationToken cancellationToken)
        => Ok(await loanService.PreviewRecalculationAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/recalculate")]
    public async Task<ActionResult<LoanDetailDto>> Recalculate(
        Guid id,
        RecalculateLoanRequest request,
        CancellationToken cancellationToken)
        => Ok(await loanService.RecalculateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await loanService.CancelAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await loanService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/agreement")]
    public async Task<IActionResult> Agreement(Guid id, CancellationToken cancellationToken)
    {
        var detail = await loanService.GetDetailAsync(id, cancellationToken);
        var templatePath = Path.Combine(environment.ContentRootPath, "Templates", "ACUERDO DE PRESTAMO.docx");
        var document = LoanAgreementDocumentBuilder.Build(detail, templatePath);

        return File(
            document,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            LoanAgreementDocumentBuilder.FileName(detail.Loan));
    }

    [HttpGet("{id:guid}/payments")]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> Payments(Guid id, CancellationToken cancellationToken)
        => Ok(await paymentService.ListByLoanAsync(id, cancellationToken));
}
