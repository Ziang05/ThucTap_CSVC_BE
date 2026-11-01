using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/export-slips")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ExportSlipsController(IServiceOrderService service) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ExportSlipDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ExportSlipDto>>> List([FromQuery] ExportSlipListQuery query, CancellationToken ct)
        => Ok(await service.ListExportsAsync(query, ct));

    [HttpGet("{code}")]
    [ProducesResponseType<ExportSlipDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExportSlipDto>> Get(string code, CancellationToken ct)
        => Ok(await service.GetExportAsync(code, ct));

    [HttpPost("{code}/result")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Result(string code, RecordExportResultRequest request, CancellationToken ct)
    {
        await service.RecordExportResultAsync(code, request, ct);
        return NoContent();
    }

    [HttpGet("{code}/history")]
    [ProducesResponseType<PagedResult<ServiceHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServiceHistoryDto>>> History(string code, [FromQuery] ServiceHistoryQuery query, CancellationToken ct)
        => Ok(await service.ExportHistoryAsync(code, query, ct));
}
