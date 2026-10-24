using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers;
using CSVC.UseCases.Manufacturers.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/history/manufacturers")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ManufacturerHistoryController(IManufacturerService manufacturers) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ManufacturerHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ManufacturerHistoryListItemDto>>> List([FromQuery] ManufacturerHistoryQuery query, CancellationToken ct)
        => Ok(await manufacturers.ListHistoryAsync(query, ct));

    [HttpGet("{historyId:guid}")]
    [ProducesResponseType<ManufacturerHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManufacturerHistoryDetailDto>> Get(Guid historyId, CancellationToken ct)
        => Ok(await manufacturers.GetHistoryAsync(historyId, ct));
}
