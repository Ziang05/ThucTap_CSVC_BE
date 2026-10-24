using CSVC.API.Filters;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories;
using CSVC.UseCases.DeviceCategories.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/device-categories")]
[TypeFilter(typeof(CategoryValidationAuditFilter), Order = -3000)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DeviceCategoriesController(ICategoryService categories) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<CategoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CategoryListItemDto>>> List([FromQuery] CategoryListQuery query, CancellationToken ct)
        => Ok(await categories.ListAsync(query, ct));

    [HttpGet("{displayCode}")]
    [ProducesResponseType<CategoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDetailDto>> Get(string displayCode, CancellationToken ct)
        => Ok(await categories.GetAsync(displayCode, ct));

    [HttpPost]
    [ProducesResponseType<CategoryDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CategoryDetailDto>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { displayCode = category.DisplayCode }, category);
    }

    [HttpPut("{displayCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string displayCode, UpdateCategoryRequest request, CancellationToken ct)
    {
        await categories.UpdateAsync(displayCode, request, ct);
        return NoContent();
    }

    [HttpPut("{displayCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(string displayCode, CancellationToken ct)
    {
        await categories.SetLockedAsync(displayCode, true, ct);
        return NoContent();
    }

    [HttpDelete("{displayCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(string displayCode, CancellationToken ct)
    {
        await categories.SetLockedAsync(displayCode, false, ct);
        return NoContent();
    }

    [HttpGet("{displayCode}/lock-impact")]
    [ProducesResponseType<CategoryLockImpactDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryLockImpactDto>> GetLockImpact(string displayCode, CancellationToken ct)
        => Ok(await categories.GetLockImpactAsync(displayCode, ct));

    [HttpGet("{displayCode}/history")]
    [ProducesResponseType<PagedResult<CategoryHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CategoryHistoryListItemDto>>> GetHistory(string displayCode,
        [FromQuery] CategoryHistoryQuery query, CancellationToken ct)
        => Ok(await categories.ListHistoryAsync(query, ct, displayCode));

    [HttpGet("{displayCode}/history/{historyId:guid}")]
    [ProducesResponseType<CategoryHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryHistoryDetailDto>> GetHistoryItem(string displayCode, Guid historyId, CancellationToken ct)
        => Ok(await categories.GetHistoryAsync(historyId, ct, displayCode));

    // Audit the forbidden request without deleting a category or changing device links.
    [HttpDelete("{displayCode}")]
    public async Task<IActionResult> DenyDelete(string displayCode, CancellationToken ct)
    {
        await categories.DenyDeleteAsync(displayCode, ct);
        return Forbid();
    }
}
