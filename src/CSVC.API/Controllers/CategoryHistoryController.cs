using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories;
using CSVC.UseCases.DeviceCategories.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/history/device-categories")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class CategoryHistoryController(ICategoryService categories) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<CategoryHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CategoryHistoryListItemDto>>> List([FromQuery] CategoryHistoryQuery query, CancellationToken ct)
        => Ok(await categories.ListHistoryAsync(query, ct));

    [HttpGet("{historyId:guid}")]
    [ProducesResponseType<CategoryHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryHistoryDetailDto>> Get(Guid historyId, CancellationToken ct)
        => Ok(await categories.GetHistoryAsync(historyId, ct));
}
