using CSVC.API.Filters;
using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers;
using CSVC.UseCases.Manufacturers.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/manufacturers")]
[TypeFilter(typeof(ManufacturerValidationAuditFilter), Order = -3000)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ManufacturersController(IManufacturerService manufacturers) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ManufacturerListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ManufacturerListItemDto>>> List([FromQuery] ManufacturerListQuery query, CancellationToken ct)
        => Ok(await manufacturers.ListAsync(query, ct));

    [HttpGet("{displayCode}")]
    [ProducesResponseType<ManufacturerDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManufacturerDetailDto>> Get(string displayCode, CancellationToken ct)
        => Ok(await manufacturers.GetAsync(displayCode, ct));

    [HttpPost]
    [ProducesResponseType<ManufacturerDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ManufacturerDetailDto>> Create(CreateManufacturerRequest request, CancellationToken ct)
    {
        var manufacturer = await manufacturers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { displayCode = manufacturer.DisplayCode }, manufacturer);
    }

    [HttpPut("{displayCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string displayCode, UpdateManufacturerRequest request, CancellationToken ct)
    {
        await manufacturers.UpdateAsync(displayCode, request, ct);
        return NoContent();
    }

    [HttpPut("{displayCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(string displayCode, CancellationToken ct)
    {
        await manufacturers.SetLockedAsync(displayCode, true, ct);
        return NoContent();
    }

    [HttpDelete("{displayCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(string displayCode, CancellationToken ct)
    {
        await manufacturers.SetLockedAsync(displayCode, false, ct);
        return NoContent();
    }

    [HttpGet("{displayCode}/lock-impact")]
    [ProducesResponseType<ManufacturerLockImpactDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManufacturerLockImpactDto>> GetLockImpact(string displayCode, CancellationToken ct)
        => Ok(await manufacturers.GetLockImpactAsync(displayCode, ct));

    [HttpGet("{displayCode}/history")]
    [ProducesResponseType<PagedResult<ManufacturerHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ManufacturerHistoryListItemDto>>> GetHistory(string displayCode,
        [FromQuery] ManufacturerHistoryQuery query, CancellationToken ct)
        => Ok(await manufacturers.ListHistoryAsync(query, ct, displayCode));

    [HttpGet("{displayCode}/history/{historyId:guid}")]
    [ProducesResponseType<ManufacturerHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManufacturerHistoryDetailDto>> GetHistoryItem(string displayCode, Guid historyId, CancellationToken ct)
        => Ok(await manufacturers.GetHistoryAsync(historyId, ct, displayCode));

    // Audit the forbidden request without deleting a manufacturer or changing device links.
    [HttpDelete("{displayCode}")]
    public async Task<IActionResult> DenyDelete(string displayCode, CancellationToken ct)
    {
        await manufacturers.DenyDeleteAsync(displayCode, ct);
        return Forbid();
    }
}
