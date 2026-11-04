using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/export-return-slips")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ExportReturnsController(IServiceOrderService service) : AuthenticatedApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExportReturnDto>>> List([FromQuery] ExportReturnListQuery query, CancellationToken ct)
        => Ok(await service.ListReturnsAsync(query, ct));
    [HttpGet("{code}")]
    public async Task<ActionResult<ExportReturnDto>> Get(string code, CancellationToken ct)
        => Ok(await service.GetReturnAsync(code, ct));
    [HttpPost]
    public async Task<ActionResult<ExportReturnDto>> Create(CreateExportReturnRequest request, CancellationToken ct)
    {
        var slip = await service.CreateReturnAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { code = slip.DisplayCode }, slip);
    }
    [HttpGet("{code}/history")]
    public async Task<ActionResult<PagedResult<ServiceHistoryDto>>> History(string code, [FromQuery] ServiceHistoryQuery query, CancellationToken ct)
        => Ok(await service.ReturnHistoryAsync(code, query, ct));
}
