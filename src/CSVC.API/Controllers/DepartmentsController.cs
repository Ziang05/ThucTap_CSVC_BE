using CSVC.API.Filters;
using CSVC.UseCases.Common;
using CSVC.UseCases.Departments;
using CSVC.UseCases.Departments.DTOs;
using CSVC.UseCases.History;
using CSVC.UseCases.History.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/departments")]
[TypeFilter(typeof(DepartmentValidationAuditFilter), Order = -3000)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DepartmentsController(IDepartmentService departments, IHistoryService history) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DepartmentListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DepartmentListItemDto>>> List([FromQuery] DepartmentListQuery query, CancellationToken cancellationToken)
        => Ok(await departments.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DepartmentDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await departments.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<DepartmentDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DepartmentDetailDto>> Create(CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var department = await departments.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        await departments.UpdateAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        await departments.SetLockedAsync(id, true, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await departments.SetLockedAsync(id, false, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/lock-impact")]
    [ProducesResponseType<DepartmentLockImpactDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentLockImpactDto>> GetLockImpact(Guid id, CancellationToken cancellationToken)
        => Ok(await departments.GetLockImpactAsync(id, cancellationToken));

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<PagedResult<DepartmentHistoryListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DepartmentHistoryListItemDto>>> GetHistory(Guid id, [FromQuery] DepartmentHistoryQuery query,
        CancellationToken cancellationToken) => Ok(await history.ListDepartmentsAsync(query, cancellationToken, id));

    [HttpGet("{id:guid}/history/{historyId:guid}")]
    [ProducesResponseType<DepartmentHistoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentHistoryDetailDto>> GetHistoryItem(Guid id, Guid historyId, CancellationToken cancellationToken)
        => Ok(await history.GetDepartmentAsync(historyId, cancellationToken, id));

    // This route only audits an explicitly forbidden request; it never deletes a row.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DenyDelete(Guid id, CancellationToken cancellationToken)
    {
        await departments.DenyDeleteAsync(id, cancellationToken);
        return Forbid();
    }
}
