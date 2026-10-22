using System.ComponentModel.DataAnnotations;
using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.History;

public sealed class HistoryService(IUnitOfWork data, ICurrentAccount current, TimeProvider clock) : IHistoryService
{
    public async Task<PagedResult<AccountHistoryListItemDto>> ListAccountsAsync(AccountHistoryQuery query, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        Validate(query);
        return await data.AccountHistories.ListAsync(query, ct);
    }

    public async Task<AccountHistoryDetailDto> GetAccountAsync(Guid id, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        return await data.AccountHistories.GetDetailAsync(id, ct) ?? throw Missing();
    }

    public async Task<PagedResult<AuthHistoryListItemDto>> ListAuthenticationAsync(AuthHistoryQuery query, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        Validate(query);
        return await data.AuthHistories.ListAsync(query, ct);
    }

    public async Task<AuthHistoryDetailDto> GetAuthenticationAsync(Guid id, CancellationToken ct)
    {
        await RequireManagerAsync(ct);
        return await data.AuthHistories.GetDetailAsync(id, ct) ?? throw Missing();
    }

    public async Task<PagedResult<DepartmentHistoryListItemDto>> ListDepartmentsAsync(DepartmentHistoryQuery query, CancellationToken ct, Guid? departmentScope = null)
    {
        await RequireManagerAsync(ct);
        Validate(query);
        await RequireDepartmentScopeAsync(departmentScope, ct);
        return await data.DepartmentHistories.ListAsync(query, ct, departmentScope);
    }

    public async Task<DepartmentHistoryDetailDto> GetDepartmentAsync(Guid id, CancellationToken ct, Guid? departmentScope = null)
    {
        await RequireManagerAsync(ct);
        await RequireDepartmentScopeAsync(departmentScope, ct);
        return await data.DepartmentHistories.GetDetailAsync(id, ct, departmentScope) ?? throw Missing();
    }

    public async Task<PagedResult<AreaHistoryListItemDto>> ListAreasAsync(AreaHistoryQuery query, CancellationToken ct, Guid? areaScope = null)
    {
        await RequireManagerAsync(ct);
        Validate(query);
        if (query.IncludeDescendants && query.AreaId is null && areaScope is null)
            throw new UseCaseException(FailureKind.Validation, "VALIDATION_FAILED", "Select an area when including descendants.");
        await RequireAreaScopeAsync(areaScope, ct);
        return await data.AreaHistories.ListAsync(query, ct, areaScope);
    }

    public async Task<AreaHistoryDetailDto> GetAreaAsync(Guid id, CancellationToken ct, Guid? areaScope = null, bool includeDescendants = false)
    {
        await RequireManagerAsync(ct);
        await RequireAreaScopeAsync(areaScope, ct);
        return await data.AreaHistories.GetDetailAsync(id, ct, areaScope, includeDescendants) ?? throw Missing();
    }

    private async Task RequireAreaScopeAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } value && !await data.Areas.ExistsAsync(a => a.Id == value, ct))
            throw new UseCaseException(FailureKind.NotFound, "AREA_NOT_FOUND", "Area was not found.");
    }

    private async Task RequireDepartmentScopeAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } value && !await data.Departments.ExistsAsync(d => d.Id == value, ct))
            throw new UseCaseException(FailureKind.NotFound, "DEPARTMENT_NOT_FOUND", "Department was not found.");
    }

    private async Task RequireManagerAsync(CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId)
            throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var account = await data.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsLock, ct);
        var session = await data.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.AccountId == id
            && s.IsActive && s.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null)
            throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var role = await data.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code != RoleCodes.EquipmentManager)
            throw new UseCaseException(FailureKind.Forbidden, "HISTORY_ACCESS_FORBIDDEN", "Only equipment managers can read history.");
    }

    private static void Validate(HistoryQuery query)
    {
        if (!Validator.TryValidateObject(query, new ValidationContext(query), [], validateAllProperties: true))
            throw new UseCaseException(FailureKind.Validation, "VALIDATION_FAILED", "History query is invalid.");
    }

    private static UseCaseException Missing() => new(FailureKind.NotFound, "HISTORY_NOT_FOUND", "History item was not found.");
}
