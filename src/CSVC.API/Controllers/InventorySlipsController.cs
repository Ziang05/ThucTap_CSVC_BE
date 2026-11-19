using CSVC.UseCases.Inventories;
using CSVC.UseCases.Common;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/inventory-slips")]
public sealed class InventorySlipsController(IInventoryService service) : AuthenticatedApiControllerBase
{
    [HttpGet] public async Task<ActionResult<PagedResult<InventorySummaryDto>>> List([FromQuery] InventoryListQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));
    [HttpGet("{id:guid}/plan")] public async Task<ActionResult<InventoryPlanDto>> Plan(Guid id, CancellationToken ct) => Ok(await service.PlanAsync(id, ct));
    [HttpPost] public async Task<ActionResult<InventorySlipDto>> Create(CreateInventoryRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Plan.SlipId }, result);
    }
    [HttpPut("{id:guid}")] public async Task<ActionResult<InventorySlipDto>> Update(Guid id, UpdateInventoryPlanRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(id, request, ct));
    [HttpPost("{id:guid}/revisions")] public async Task<ActionResult<InventorySlipDto>> Propose(Guid id, UpdateInventoryPlanRequest request, CancellationToken ct) => Ok(await service.ProposeAsync(id, request, ct));
    [HttpPost("{id:guid}/revisions/{revisionId:guid}/publish")] public async Task<ActionResult<InventorySlipDto>> Publish(Guid id, Guid revisionId, InventoryCommand request, CancellationToken ct) => Ok(await service.PublishAsync(id, revisionId, request, ct));
    [HttpPost("{id:guid}/revisions/{revisionId:guid}/reject")] public async Task<ActionResult<InventorySlipDto>> Reject(Guid id, Guid revisionId, InventoryReasonRequest request, CancellationToken ct) => Ok(await service.RejectRevisionAsync(id, revisionId, request, ct));
    [HttpPost("{id:guid}/cancel")] public async Task<ActionResult<InventorySlipDto>> Cancel(Guid id, InventoryReasonRequest request, CancellationToken ct) => Ok(await service.CancelAsync(id, request, ct));
    [HttpPost("{id:guid}/start")] public async Task<ActionResult<InventorySlipDto>> Start(Guid id, InventoryCommand request, CancellationToken ct) => Ok(await service.StartAsync(id, request, ct));
    [HttpPost("{id:guid}/complete")] public async Task<ActionResult<InventorySlipDto>> Complete(Guid id, InventoryCommand request, CancellationToken ct) => Ok(await service.CompleteAsync(id, request, ct));
    [HttpPost("{id:guid}/items/{itemId:guid}/results")] public async Task<ActionResult<InventoryResultDto>> Record(Guid id, Guid itemId, RecordInventoryResultRequest request, CancellationToken ct) => Ok(await service.RecordAsync(id, itemId, request, ct));
    [HttpPatch("{id:guid}/items/{itemId:guid}/result")] public async Task<ActionResult<InventoryResultDto>> Correct(Guid id, Guid itemId, CorrectInventoryResultRequest request, CancellationToken ct) => Ok(await service.CorrectAsync(id, itemId, request, ct));
    [HttpGet("{id:guid}/progress")] public async Task<ActionResult<InventoryProgressDto>> Progress(Guid id, CancellationToken ct) => Ok(await service.ProgressAsync(id, ct));
    [HttpGet("{id:guid}/history")] public async Task<ActionResult<PagedResult<InventoryHistoryDto>>> History(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.HistoryAsync(id, page, pageSize, ct));
    [HttpGet("{id:guid}/items/{itemId:guid}/history")] public async Task<ActionResult<PagedResult<InventoryItemHistoryDto>>> ItemHistory(Guid id, Guid itemId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.ItemHistoryAsync(id, itemId, page, pageSize, ct));
    [HttpGet("{id:guid}/report")] public async Task<ActionResult<InventoryReportDto>> Report(Guid id, CancellationToken ct) => Ok(await service.ReportAsync(id, ct));
    [HttpGet("{id:guid}/report.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return File(await service.ExportAsync(id, ct), "text/csv; charset=utf-8", $"inventory-{id:N}.csv");
    }
}

[Route("api/v1/inventory-notice-policy")]
public sealed class InventoryNoticePolicyController(IInventoryService service) : AuthenticatedApiControllerBase
{
    [HttpGet] public async Task<ActionResult<InventoryPolicyDto>> Get(CancellationToken ct) => Ok(await service.PolicyAsync(ct));
    [HttpPut] public async Task<ActionResult<InventoryPolicyDto>> Update(UpdateInventoryPolicyRequest request, CancellationToken ct) => Ok(await service.UpdatePolicyAsync(request, ct));
    [HttpGet("history")] public async Task<ActionResult<PagedResult<InventoryPolicyHistoryDto>>> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.PolicyHistoryAsync(page, pageSize, ct));
}

[Route("api/v1/inventory-notifications")]
public sealed class InventoryNotificationsController(IInventoryService service) : AuthenticatedApiControllerBase
{
    [HttpGet] public async Task<ActionResult<PagedResult<InventoryNotificationDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.NotificationsAsync(page, pageSize, ct));
    [HttpPost("{id:guid}/read")] public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    { await service.MarkReadAsync(id, ct); return NoContent(); }
}
