using CSVC.UseCases.Common;
using CSVC.UseCases.DamageReports;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/damage-reports")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DamageReportsController(IDamageReportService reports) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DamageReportSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DamageReportSummaryDto>>> List([FromQuery] DamageReportListQuery query, CancellationToken ct)
        => Ok(await reports.ListAsync(query, ct));

    [HttpGet("devices")]
    [ProducesResponseType<PagedResult<ReportDeviceOptionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReportDeviceOptionDto>>> Devices([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
        => Ok(await reports.DevicesAsync(page, pageSize, search, ct));

    [HttpGet("{reportCode}")]
    [ProducesResponseType<DamageReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DamageReportDto>> Get(string reportCode, CancellationToken ct) => Ok(await reports.GetAsync(reportCode, ct));

    [HttpPost]
    [RequestSizeLimit(40 * 1024 * 1024)]
    [ProducesResponseType<DamageReportDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DamageReportDto>> Create(CreateDamageReportRequest request, CancellationToken ct)
    {
        var result = await reports.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { reportCode = result.ReportCode }, result);
    }

    [HttpPut("{reportCode}")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    [ProducesResponseType<DamageReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DamageReportDto>> Update(string reportCode, UpdateDamageReportRequest request, CancellationToken ct)
        => Ok(await reports.UpdateAsync(reportCode, request, ct));

    [HttpPost("{reportCode}/receive")]
    [ProducesResponseType<DamageReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DamageReportDto>> Receive(string reportCode, ReceiveDamageReportRequest request, CancellationToken ct)
        => Ok(await reports.ReceiveAsync(reportCode, request, ct));

    [HttpGet("{reportCode}/history")]
    [ProducesResponseType<PagedResult<ReportHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReportHistoryDto>>> History(string reportCode, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await reports.HistoryAsync(reportCode, page, pageSize, ct));

    [HttpGet("{reportCode}/evidence/{sequence:int:min(1)}")]
    [Produces("image/png", "image/jpeg")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Evidence(string reportCode, int sequence, CancellationToken ct)
    {
        var image = await reports.EvidenceAsync(reportCode, sequence, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType);
    }
}
