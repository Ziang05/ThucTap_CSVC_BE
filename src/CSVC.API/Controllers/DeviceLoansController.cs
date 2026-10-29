using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceLoans;
using CSVC.UseCases.DeviceLoans.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/device-loans")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DeviceLoansController(IDeviceLoanService loans) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DeviceLoanDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceLoanDto>>> List([FromQuery] DeviceLoanListQuery query, CancellationToken cancellationToken)
        => Ok(await loans.ListAsync(query, cancellationToken));

    [HttpGet("{loanCode}")]
    [ProducesResponseType<DeviceLoanDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeviceLoanDto>> Get(string loanCode, CancellationToken cancellationToken)
        => Ok(await loans.GetAsync(loanCode, cancellationToken));

    [HttpPost]
    [ProducesResponseType<DeviceLoanDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DeviceLoanDto>> Create(CreateDeviceLoanRequest request, CancellationToken cancellationToken)
    {
        var loan = await loans.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { loanCode = loan.LoanCode }, loan);
    }

    [HttpPut("{loanCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string loanCode, UpdateDeviceLoanRequest request, CancellationToken cancellationToken)
    {
        await loans.UpdateAsync(loanCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{loanCode}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(string loanCode, [FromBody] LoanDecisionRequest? request, CancellationToken cancellationToken)
    {
        await loans.DecideAsync(loanCode, true, request ?? new(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{loanCode}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(string loanCode, [FromBody] LoanDecisionRequest? request, CancellationToken cancellationToken)
    {
        await loans.DecideAsync(loanCode, false, request ?? new(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{loanCode}/handover-complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> HandoverComplete(string loanCode, LoanHandoverRequest request, CancellationToken cancellationToken)
    {
        await loans.HandoverCompleteAsync(loanCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{loanCode}/return")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Return(string loanCode, LoanReturnRequest request, CancellationToken cancellationToken)
    {
        await loans.ReturnAsync(loanCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{loanCode}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(string loanCode, CancellationToken cancellationToken)
    {
        await loans.CancelAsync(loanCode, cancellationToken);
        return NoContent();
    }

    [HttpGet("{loanCode}/history")]
    [ProducesResponseType<PagedResult<DeviceLoanHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceLoanHistoryDto>>> History(string loanCode, [FromQuery] DeviceLoanHistoryQuery query, CancellationToken cancellationToken)
        => Ok(await loans.HistoryAsync(loanCode, query, cancellationToken));
}
