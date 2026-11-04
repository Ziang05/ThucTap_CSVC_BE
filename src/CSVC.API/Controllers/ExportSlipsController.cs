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

    [HttpPost]
    public async Task<ActionResult<ExportSlipDto>> Create(CreateExportSlipRequest request, CancellationToken ct)
    {
        var slip = await service.CreateExportAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { code = slip.DisplayCode }, slip);
    }

    [HttpPost("{code}/approve")]
    public async Task<IActionResult> Approve(string code, ServiceTransitionRequest request, CancellationToken ct)
    { await service.ApproveExportAsync(code, request, ct); return NoContent(); }

    [HttpPut("{code}")]
    public async Task<IActionResult> Edit(string code, EditExportSlipRequest request, CancellationToken ct)
    { await service.EditExportAsync(code, request, ct); return NoContent(); }

    [HttpPost("{code}/cancel")]
    public async Task<IActionResult> Cancel(string code, CancelServiceOrderRequest request, CancellationToken ct)
    { await service.CancelExportAsync(code, request, ct); return NoContent(); }

    [HttpGet("{code}/return-eligible-items")]
    public async Task<IActionResult> ReturnEligible(string code, CancellationToken ct)
        => Ok((await service.GetExportAsync(code, ct)).Items.Where(i => i.Result == CSVC.Core.ServiceOrders.ExportResult.Success && !i.Returned));

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
