using CSVC.Core.ProcurementRequests;
using CSVC.UseCases.Common;
using CSVC.UseCases.ProcurementRequests;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class ProcurementRequestRepository(CsvcDbContext db) : IProcurementRequestRepository
{
    public Task<ProcurementRequest?> GetAsync(string code, bool tracked, CancellationToken ct)
        => (tracked ? db.Set<ProcurementRequest>() : db.Set<ProcurementRequest>().AsNoTracking()).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.DisplayCode == code.Trim().ToUpperInvariant(), ct);
    public Task<ProcurementRequest?> GetBySourceAsync(string source, string externalCode, CancellationToken ct)
        => db.Set<ProcurementRequest>().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Source == source && x.ExternalCode == externalCode, ct);
    public async Task<PagedResult<ProcurementSummary>> ListAsync(ProcurementQuery query, CancellationToken ct, string? departmentCode = null, Guid? assigneeId = null)
    {
        var source = db.Set<ProcurementRequest>().AsNoTracking();
        if (departmentCode is not null) source = source.Where(x => x.RequestingDepartmentCode == departmentCode);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.DisplayCode.ToLower().Contains(term) || x.ExternalCode.ToLower().Contains(term) || x.Title.ToLower().Contains(term));
        }
        if (query.RequestType is { } type) source = source.Where(x => x.RequestType == type);
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (query.RequiresReview is { } review) source = source.Where(x => x.RequiresReview == review);
        if (query.Unassigned) source = source.Where(x => x.AssigneeAccountId == null);
        if (assigneeId is not null) source = source.Where(x => x.AssigneeAccountId == assigneeId);
        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.DisplayCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ProcurementSummary(x.DisplayCode, x.Source, x.ExternalCode, x.RequestType, x.Title, x.RequestingDepartmentCode, x.Status, x.RequiresReview, x.SourceVersion, x.Version, x.ReceivedAt)
            { AssigneeAccountCode = db.Accounts.Where(a => a.Id == x.AssigneeAccountId).Select(a => a.AccountCode).FirstOrDefault(),
              AssigneeName = (from a in db.Accounts join p in db.UserProfiles on a.UserProfileId equals p.Id where a.Id == x.AssigneeAccountId select p.FullName).FirstOrDefault() }).ToListAsync(ct);
        return new(items, count, query.Page, query.PageSize);
    }
    public async Task<PagedResult<ProcurementEventDto>> HistoryAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        var source = db.Set<ProcurementRequestEvent>().AsNoTracking().Where(x => x.RequestId == id);
        var count = await source.CountAsync(ct);
        var events = await (from e in source
                            join account in db.Accounts.AsNoTracking() on e.ActorAccountId equals account.Id
                            orderby e.OccurredAt descending, e.Id
                            select new ProcurementEventDto(e.ExternalEventId, e.SourceVersion, e.Action, e.Outcome, e.Reason, e.OccurredAt, account.AccountCode, e.Payload))
                            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(events, count, page, pageSize);
    }
}
