using CSVC.UseCases.Common;
using CSVC.UseCases.History;
using CSVC.UseCases.History.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/history")]
public sealed class HistoryController(IHistoryService history) : AuthenticatedApiControllerBase
{
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
}
