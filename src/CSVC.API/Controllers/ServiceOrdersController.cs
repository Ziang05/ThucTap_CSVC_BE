using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/service-orders")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ServiceOrdersController(IServiceOrderService service) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ServiceOrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServiceOrderDto>>> List([FromQuery] ServiceOrderListQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{code}")]
    [ProducesResponseType<ServiceOrderDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceOrderDto>> Get(string code, CancellationToken ct)
        => Ok(await service.GetAsync(code, ct));

    [HttpPost]
    [ProducesResponseType<ServiceOrderDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ServiceOrderDto>> Create(CreateServiceOrderRequest request, CancellationToken ct)
    {
        var order = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { code = order.DisplayCode }, order);
    }

    [HttpPost("{code}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(string code, ServiceTransitionRequest request, CancellationToken ct)
    {
        await service.ApproveAsync(code, request, ct);
        return NoContent();
    }

    [HttpPost("{code}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(string code, CancelServiceOrderRequest request, CancellationToken ct)
    {
        await service.CancelAsync(code, request, ct);
        return NoContent();
    }

    [HttpPost("{code}/results")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Results(string code, RecordServiceResultsRequest request, CancellationToken ct)
    {
        await service.RecordResultsAsync(code, request, ct);
        return NoContent();
    }

    [HttpGet("{code}/history")]
    [ProducesResponseType<PagedResult<ServiceHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServiceHistoryDto>>> History(string code, [FromQuery] ServiceHistoryQuery query, CancellationToken ct)
        => Ok(await service.HistoryAsync(code, query, ct));
}
