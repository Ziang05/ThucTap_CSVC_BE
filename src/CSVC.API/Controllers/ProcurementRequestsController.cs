using CSVC.UseCases.ProcurementRequests;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/procurement-requests")]
public sealed class ProcurementRequestsController(IProcurementRequestService service) : AuthenticatedApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ProcurementQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, ct));
    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct) => Ok(await service.GetAsync(code, ct));
    [HttpGet("{code}/stock")]
    public async Task<IActionResult> Stock(string code, CancellationToken ct) => Ok(await service.StockAsync(code, ct));
    [HttpPost("{code}/track-purchase")]
    public async Task<IActionResult> TrackPurchase(string code, TrackPurchaseRequest input, CancellationToken ct)
    { await service.TrackPurchaseAsync(code, input, ct); return NoContent(); }
    [HttpPost("{code}/receive")]
    public async Task<IActionResult> ReceivePurchase(string code, ReceivePurchaseRequest input, CancellationToken ct)
    { await service.ReceivePurchaseAsync(code, input, ct); return NoContent(); }
    [HttpGet("assignees")]
    public async Task<IActionResult> Assignees(CancellationToken ct) => Ok(await service.AssigneesAsync(ct));
    [HttpGet("workload")]
    public async Task<IActionResult> Workload(CancellationToken ct) => Ok(await service.WorkloadAsync(ct));
    [HttpPost("{code}/assignment")]
    public async Task<IActionResult> Assign(string code, AssignSourceRequest input, CancellationToken ct)
    { await service.AssignAsync(code, input, ct); return NoContent(); }
    [HttpPost("{code}/start-repair")]
    public async Task<IActionResult> StartRepair(string code, StartSourceRepair input, CancellationToken ct)
        => Ok(await service.StartRepairAsync(code, input, ct));
    [HttpGet("{code}/history")]
    public async Task<IActionResult> History(string code, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await service.HistoryAsync(code, page, pageSize, ct));
    [HttpPost("simulated-events")]
    public async Task<IActionResult> Simulate(PmmsSimulatedEvent input, CancellationToken ct) => Ok(await service.SimulateAsync(input, ct));
    [HttpPost("{code}/status")]
    public async Task<IActionResult> Status(string code, ProcurementTransition input, CancellationToken ct)
    { await service.TransitionAsync(code, input, ct); return NoContent(); }
    [HttpPost("{code}/review-source-update")]
    public async Task<IActionResult> Review(string code, ProcurementTransition input, CancellationToken ct)
    { await service.ReviewAsync(code, input, ct); return NoContent(); }
    [HttpPost("{code}/complete")]
    public async Task<IActionResult> Complete(string code, ProcurementClose input, CancellationToken ct)
    { await service.CompleteAsync(code, input, ct); return NoContent(); }
    [HttpPost("{code}/service-evidence")]
    public async Task<IActionResult> ServiceEvidence(string code, RecordServiceEvidence input, CancellationToken ct)
    { await service.RecordServicesAsync(code, input, ct); return NoContent(); }
    [HttpPost("{code}/accept-results")]
    public async Task<IActionResult> AcceptResults(string code, AcceptRequestResults input, CancellationToken ct)
    { await service.AcceptResultsAsync(code, input, ct); return NoContent(); }
}
