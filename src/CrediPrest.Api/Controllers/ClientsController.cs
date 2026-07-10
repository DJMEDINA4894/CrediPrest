using CrediPrest.Application.DTOs.Clients;
using CrediPrest.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrediPrest.Api.Controllers;

[ApiController]
[Authorize(Policy = "BackOffice")]
[Route("api/[controller]")]
public sealed class ClientsController(IClientService clientService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> Search([FromQuery] string? search, CancellationToken cancellationToken)
        => Ok(await clientService.SearchAsync(search, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await clientService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create(CreateClientRequest request, CancellationToken cancellationToken)
    {
        var client = await clientService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = client.Id }, client);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Update(Guid id, UpdateClientRequest request, CancellationToken cancellationToken)
        => Ok(await clientService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<ClientDto>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await clientService.SetActiveAsync(id, isActive: true, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<ClientDto>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await clientService.SetActiveAsync(id, isActive: false, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await clientService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
