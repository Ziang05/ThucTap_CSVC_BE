using CSVC.UseCases.Common;
using CSVC.UseCases.Suppliers;
using CSVC.UseCases.Suppliers.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/history/suppliers")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class SupplierHistoryController(ISupplierService suppliers) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupplierHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierHistoryListItemDto>>> List([FromQuery] SupplierHistoryQuery query, CancellationToken ct)
        => Ok(await suppliers.ListHistoryAsync(query, ct));

    [HttpGet("{historyId:guid}")]
    [ProducesResponseType<SupplierHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupplierHistoryDetailDto>> Get(Guid historyId, CancellationToken ct)
        => Ok(await suppliers.GetHistoryAsync(historyId, ct));
}
