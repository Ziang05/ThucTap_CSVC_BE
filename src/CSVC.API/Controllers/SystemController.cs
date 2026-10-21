using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("info")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo() => Ok(new SystemInfoResponse("CSVC API", "v1"));
}

public sealed record SystemInfoResponse(string Name, string ApiVersion);
