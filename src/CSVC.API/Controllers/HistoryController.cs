using CSVC.UseCases.Common;
using CSVC.UseCases.History;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.Devices;
using CSVC.UseCases.Devices.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/history")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class HistoryController(IHistoryService history, IDeviceService devices) : AuthenticatedApiControllerBase
{
    [HttpGet("devices")]
    [ProducesResponseType<PagedResult<DeviceHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceHistoryDto>>> Devices([FromQuery] DeviceHistoryQuery query, CancellationToken ct)
        => Ok(await devices.GlobalHistoryAsync(query, ct));

    [HttpGet("accounts")]
    [ProducesResponseType<PagedResult<AccountHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccountHistoryListItemDto>>> Accounts([FromQuery] AccountHistoryQuery query, CancellationToken ct)
        => Ok(await history.ListAccountsAsync(query, ct));

    [HttpGet("accounts/{id:guid}")]
    [ProducesResponseType<AccountHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountHistoryDetailDto>> Account(Guid id, CancellationToken ct)
        => Ok(await history.GetAccountAsync(id, ct));

    [HttpGet("auth")]
    [ProducesResponseType<PagedResult<AuthHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuthHistoryListItemDto>>> Authentication([FromQuery] AuthHistoryQuery query, CancellationToken ct)
        => Ok(await history.ListAuthenticationAsync(query, ct));

    [HttpGet("auth/{id:guid}")]
    [ProducesResponseType<AuthHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthHistoryDetailDto>> AuthenticationItem(Guid id, CancellationToken ct)
        => Ok(await history.GetAuthenticationAsync(id, ct));

    [HttpGet("departments")]
    [ProducesResponseType<PagedResult<DepartmentHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DepartmentHistoryListItemDto>>> Departments([FromQuery] DepartmentHistoryQuery query, CancellationToken ct)
        => Ok(await history.ListDepartmentsAsync(query, ct));

    [HttpGet("departments/{id:guid}")]
    [ProducesResponseType<DepartmentHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentHistoryDetailDto>> Department(Guid id, CancellationToken ct)
        => Ok(await history.GetDepartmentAsync(id, ct));

    [HttpGet("areas")]
    [ProducesResponseType<PagedResult<AreaHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AreaHistoryListItemDto>>> Areas([FromQuery] AreaHistoryQuery query, CancellationToken ct)
        => Ok(await history.ListAreasAsync(query, ct));

    [HttpGet("areas/{id:guid}")]
    [ProducesResponseType<AreaHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AreaHistoryDetailDto>> Area(Guid id, CancellationToken ct)
        => Ok(await history.GetAreaAsync(id, ct));
}
