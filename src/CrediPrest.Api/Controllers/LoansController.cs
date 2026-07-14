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
    INotificationService notificationService,
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
    {
        var loan = await loanService.UpdateAsync(id, request, cancellationToken);
        await notificationService.RefreshAutomaticAsync(cancellationToken);
        return Ok(loan);
    }

    [HttpPost("{id:guid}/extraordinary-payment/preview")]
    public async Task<ActionResult<LoanRecalculationPreviewDto>> PreviewExtraordinaryPayment(
        Guid id,
        ExtraordinaryPaymentPreviewRequest request,
        CancellationToken cancellationToken)
        => Ok(await loanService.PreviewExtraordinaryPaymentAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/extraordinary-payment")]
    public async Task<ActionResult<LoanDetailDto>> RegisterExtraordinaryPayment(
        Guid id,
        RegisterExtraordinaryPaymentRequest request,
        CancellationToken cancellationToken)
        => Ok(await loanService.RegisterExtraordinaryPaymentAsync(id, request, cancellationToken));

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

    [HttpGet("{id:guid}/payment-plan.pdf")]
    public async Task<IActionResult> PaymentPlanPdf(Guid id, CancellationToken cancellationToken)
    {
        var detail = await loanService.GetDetailAsync(id, cancellationToken);
        return File(
            LoanPaymentTablePdfBuilder.Build(detail),
            "application/pdf",
            LoanPaymentTablePdfBuilder.FileName(detail.Loan));
    }

    [HttpGet("{id:guid}/payments")]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> Payments(Guid id, CancellationToken cancellationToken)
        => Ok(await paymentService.ListByLoanAsync(id, cancellationToken));
}
