using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceReceipts;
using CSVC.UseCases.DeviceReceipts.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/device-receipts")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DeviceReceiptsController(IDeviceReceiptService receipts) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DeviceReceiptDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceReceiptDto>>> List([FromQuery] DeviceReceiptListQuery query, CancellationToken cancellationToken)
        => Ok(await receipts.ListAsync(query, cancellationToken));

    [HttpGet("{receiptCode}")]
    [ProducesResponseType<DeviceReceiptDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeviceReceiptDto>> Get(string receiptCode, CancellationToken cancellationToken)
        => Ok(await receipts.GetAsync(receiptCode, cancellationToken));

    [HttpPost]
    [ProducesResponseType<DeviceReceiptDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DeviceReceiptDto>> Create(CreateDeviceReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = await receipts.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { receiptCode = receipt.ReceiptCode }, receipt);
    }

    [HttpPut("{receiptCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string receiptCode, UpdateDeviceReceiptRequest request, CancellationToken cancellationToken)
    {
        await receipts.UpdateAsync(receiptCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{receiptCode}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(string receiptCode, [FromBody] ReceiptDecisionRequest? request, CancellationToken cancellationToken)
    {
        await receipts.DecideAsync(receiptCode, true, request ?? new(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{receiptCode}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(string receiptCode, [FromBody] ReceiptDecisionRequest? request, CancellationToken cancellationToken)
    {
        await receipts.DecideAsync(receiptCode, false, request ?? new(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{receiptCode}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Complete(string receiptCode, CancellationToken cancellationToken)
    {
        await receipts.CompleteAsync(receiptCode, cancellationToken);
        return NoContent();
    }

    [HttpPost("{receiptCode}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(string receiptCode, CancellationToken cancellationToken)
    {
        await receipts.CancelAsync(receiptCode, cancellationToken);
        return NoContent();
    }

    [HttpGet("{receiptCode}/history")]
    [ProducesResponseType<PagedResult<DeviceReceiptHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceReceiptHistoryDto>>> History(string receiptCode, [FromQuery] DeviceReceiptHistoryQuery query, CancellationToken cancellationToken)
        => Ok(await receipts.HistoryAsync(receiptCode, query, cancellationToken));
}
