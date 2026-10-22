using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.History;

public interface IHistoryService
{
    Task<PagedResult<AccountHistoryListItemDto>> ListAccountsAsync(AccountHistoryQuery query, CancellationToken ct);
    Task<AccountHistoryDetailDto> GetAccountAsync(Guid id, CancellationToken ct);
    Task<PagedResult<AuthHistoryListItemDto>> ListAuthenticationAsync(AuthHistoryQuery query, CancellationToken ct);
    Task<AuthHistoryDetailDto> GetAuthenticationAsync(Guid id, CancellationToken ct);
    Task<PagedResult<DepartmentHistoryListItemDto>> ListDepartmentsAsync(DepartmentHistoryQuery query, CancellationToken ct, Guid? departmentScope = null);
    Task<DepartmentHistoryDetailDto> GetDepartmentAsync(Guid id, CancellationToken ct, Guid? departmentScope = null);
    Task<PagedResult<AreaHistoryListItemDto>> ListAreasAsync(AreaHistoryQuery query, CancellationToken ct, Guid? areaScope = null);
    Task<AreaHistoryDetailDto> GetAreaAsync(Guid id, CancellationToken ct, Guid? areaScope = null, bool includeDescendants = false);
}
