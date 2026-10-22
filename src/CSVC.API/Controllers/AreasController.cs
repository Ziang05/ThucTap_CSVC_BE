using CSVC.API.Filters;
using CSVC.UseCases.Areas;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;
using CSVC.UseCases.History;
using CSVC.UseCases.History.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/areas")]
[TypeFilter(typeof(AreaValidationAuditFilter), Order = -3000)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class AreasController(IAreaService areas, IHistoryService history) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AreaListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AreaListItemDto>>> List([FromQuery] AreaListQuery query, CancellationToken cancellationToken)
        => Ok(await areas.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}/children")]
    [ProducesResponseType<PagedResult<AreaListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AreaListItemDto>>> GetChildren(Guid id, [FromQuery] AreaListQuery query,
        CancellationToken cancellationToken) => Ok(await areas.GetChildrenAsync(id, query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AreaDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AreaDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await areas.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<AreaDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AreaDetailDto>> Create(CreateAreaRequest request, CancellationToken cancellationToken)
    {
        var result = await areas.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, UpdateAreaRequest request, CancellationToken cancellationToken)
    {
        await areas.UpdateAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        await areas.SetLockedAsync(id, true, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await areas.SetLockedAsync(id, false, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/lock-impact")]
    [ProducesResponseType<AreaLockImpactDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AreaLockImpactDto>> GetLockImpact(Guid id, CancellationToken cancellationToken)
        => Ok(await areas.GetLockImpactAsync(id, cancellationToken));

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<PagedResult<AreaHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AreaHistoryListItemDto>>> GetHistory(Guid id, [FromQuery] AreaHistoryQuery query,
        CancellationToken cancellationToken) => Ok(await history.ListAreasAsync(query, cancellationToken, id));

    [HttpGet("{id:guid}/history/{historyId:guid}")]
    [ProducesResponseType<AreaHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AreaHistoryDetailDto>> GetHistoryItem(Guid id, Guid historyId,
        [FromQuery] bool includeDescendants, CancellationToken cancellationToken)
        => Ok(await history.GetAreaAsync(historyId, cancellationToken, id, includeDescendants));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DenyDelete(Guid id, CancellationToken cancellationToken)
    {
        await areas.DenyDeleteAsync(id, cancellationToken);
        return Forbid();
    }
}
