using CSVC.UseCases.Common;
using CSVC.UseCases.DamageReports;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/damage-sessions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DamageSessionsController(IDamageReportService reports) : AuthenticatedApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DamageSessionDto>>> List([FromQuery] DamageSessionQuery query, CancellationToken ct)
        => Ok(await reports.SessionsAsync(query, ct));

    [HttpGet("repair-options")]
    public async Task<ActionResult<IReadOnlyList<DamageSessionOptionDto>>> RepairOptions([FromQuery] string[] deviceCodes, CancellationToken ct)
        => Ok(await reports.RepairOptionsAsync(deviceCodes, ct));

    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<DamageSessionDto>> Detail(Guid sessionId, CancellationToken ct)
        => Ok(await reports.SessionAsync(sessionId, ct));
}
