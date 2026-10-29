using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceTransfers;
using CSVC.UseCases.DeviceTransfers.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/device-transfers")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DeviceTransfersController(IDeviceTransferService transfers) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DeviceTransferDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceTransferDto>>> List([FromQuery] DeviceTransferListQuery query, CancellationToken cancellationToken)
        => Ok(await transfers.ListAsync(query, cancellationToken));

    [HttpGet("{transferCode}")]
    [ProducesResponseType<DeviceTransferDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeviceTransferDto>> Get(string transferCode, CancellationToken cancellationToken)
        => Ok(await transfers.GetAsync(transferCode, cancellationToken));

    [HttpPost]
    [ProducesResponseType<DeviceTransferDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DeviceTransferDto>> Create(CreateDeviceTransferRequest request, CancellationToken cancellationToken)
    {
        var transfer = await transfers.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { transferCode = transfer.TransferCode }, transfer);
    }

    [HttpPut("{transferCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string transferCode, UpdateDeviceTransferRequest request, CancellationToken cancellationToken)
    {
        await transfers.UpdateAsync(transferCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{transferCode}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(string transferCode, [FromBody] TransferDecisionRequest? request, CancellationToken cancellationToken)
    {
        await transfers.DecideAsync(transferCode, true, request ?? new(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{transferCode}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(string transferCode, [FromBody] TransferDecisionRequest? request, CancellationToken cancellationToken)
    {
        await transfers.DecideAsync(transferCode, false, request ?? new(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{transferCode}/handover-complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> HandoverComplete(string transferCode, TransferHandoverRequest request, CancellationToken cancellationToken)
    {
        await transfers.HandoverCompleteAsync(transferCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{transferCode}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(string transferCode, CancellationToken cancellationToken)
    {
        await transfers.CancelAsync(transferCode, cancellationToken);
        return NoContent();
    }

    [HttpGet("{transferCode}/history")]
    [ProducesResponseType<PagedResult<DeviceTransferHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceTransferHistoryDto>>> History(string transferCode, [FromQuery] DeviceTransferHistoryQuery query, CancellationToken cancellationToken)
        => Ok(await transfers.HistoryAsync(transferCode, query, cancellationToken));
}
