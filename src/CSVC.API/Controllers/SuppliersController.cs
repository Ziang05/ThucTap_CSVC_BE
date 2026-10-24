using CSVC.API.Filters;
using CSVC.UseCases.Common;
using CSVC.UseCases.Suppliers;
using CSVC.UseCases.Suppliers.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/suppliers")]
[TypeFilter(typeof(SupplierValidationAuditFilter), Order = -3000)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class SuppliersController(ISupplierService suppliers) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupplierListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierListItemDto>>> List([FromQuery] SupplierListQuery query, CancellationToken ct)
        => Ok(await suppliers.ListAsync(query, ct));

    [HttpGet("{displayCode}")]
    [ProducesResponseType<SupplierDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierDetailDto>> Get(string displayCode, CancellationToken ct)
        => Ok(await suppliers.GetAsync(displayCode, ct));

    [HttpPost]
    [ProducesResponseType<SupplierDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SupplierDetailDto>> Create(CreateSupplierRequest request, CancellationToken ct)
    {
        var supplier = await suppliers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { displayCode = supplier.DisplayCode }, supplier);
    }

    [HttpPut("{displayCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string displayCode, UpdateSupplierRequest request, CancellationToken ct)
    {
        await suppliers.UpdateAsync(displayCode, request, ct);
        return NoContent();
    }

    [HttpPut("{displayCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(string displayCode, CancellationToken ct)
    {
        await suppliers.SetLockedAsync(displayCode, true, ct);
        return NoContent();
    }

    [HttpDelete("{displayCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(string displayCode, CancellationToken ct)
    {
        await suppliers.SetLockedAsync(displayCode, false, ct);
        return NoContent();
    }

    [HttpGet("{displayCode}/lock-impact")]
    [ProducesResponseType<SupplierLockImpactDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierLockImpactDto>> GetLockImpact(string displayCode, CancellationToken ct)
        => Ok(await suppliers.GetLockImpactAsync(displayCode, ct));

    [HttpGet("{displayCode}/history")]
    [ProducesResponseType<PagedResult<SupplierHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierHistoryListItemDto>>> GetHistory(string displayCode,
        [FromQuery] SupplierHistoryQuery query, CancellationToken ct)
        => Ok(await suppliers.ListHistoryAsync(query, ct, displayCode));

    [HttpGet("{displayCode}/history/{historyId:guid}")]
    [ProducesResponseType<SupplierHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierHistoryDetailDto>> GetHistoryItem(string displayCode, Guid historyId, CancellationToken ct)
        => Ok(await suppliers.GetHistoryAsync(historyId, ct, displayCode));

    // Audit the forbidden request without deleting a supplier or changing device links.
    [HttpDelete("{displayCode}")]
    public async Task<IActionResult> DenyDelete(string displayCode, CancellationToken ct)
    {
        await suppliers.DenyDeleteAsync(displayCode, ct);
        return Forbid();
    }
}
