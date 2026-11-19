using System.Data;
using System.Linq.Expressions;
using CSVC.Core.Common;
using CSVC.Core.Inventories;
using CSVC.UseCases.Common;
using CSVC.UseCases.Inventories;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class EquipmentInventoryRepository(CsvcDbContext db) : IEquipmentInventoryRepository
{
    public async Task<InventorySlip?> LoadAsync(Guid id, bool tracked, CancellationToken ct)
    {
        // Split collection reads share one MVCC snapshot, including concurrent publication and corrections.
        await using var snapshot = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        IQueryable<InventorySlip> query = db.InventorySlips;
        if (!tracked) query = query.AsNoTracking();
        var slip = await query.Include(x => x.Revisions).ThenInclude(x => x.Items).ThenInclude(x => x.Result)
            .Include(x => x.Revisions).ThenInclude(x => x.Assignees).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (snapshot is not null) await snapshot.CommitAsync(ct);
        return slip;
    }

    public async Task<PagedResult<InventorySummaryDto>> ListAsync(InventoryListQuery q, Guid? departmentScope, CancellationToken ct)
    {
        var slips = db.InventorySlips.AsNoTracking().AsQueryable();
        if (departmentScope is { } scope) slips = slips.Where(x => x.DepartmentId == scope && x.Status == InventoryStates.Preparing);
        if (q.DepartmentId is { } department) slips = slips.Where(x => x.DepartmentId == department);
        if (q.Status is { } state) slips = slips.Where(x => x.Status == state);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim().ToLower();
            slips = slips.Where(x => x.DisplayCode.ToLower().Contains(search) || x.DepartmentNameSnapshot.ToLower().Contains(search));
        }
        var query = slips.Select(x => new { Slip = x, Revision = x.Revisions
            .Where(r => departmentScope == null || r.Status == InventoryStates.Published)
            .OrderByDescending(r => r.Status == InventoryStates.Published).ThenByDescending(r => r.RevisionNo).First() });
        if (q.From is { } from) query = query.Where(x => x.Revision.PlannedStartAt >= from);
        if (q.To is { } to) query = query.Where(x => x.Revision.PlannedStartAt <= to);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Slip.CreatedAt).ThenBy(x => x.Slip.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(x => new InventorySummaryDto(x.Slip.Id, x.Slip.DisplayCode, x.Slip.Status,
                x.Slip.DepartmentCodeSnapshot, x.Slip.DepartmentNameSnapshot, x.Revision.PlannedStartAt, x.Revision.Items.Count, x.Slip.Version)).ToListAsync(ct);
        return new(items, count, q.Page, q.PageSize);
    }

    public async Task<InventoryRequestReceipt?> RequestAsync(Guid requestId, CancellationToken ct)
    {
        var slip = await db.InventoryHistories.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId, ct);
        if (slip is not null) return new(slip.ActorAccountId, slip.ActionCode, slip.RequestHash, slip.ResponseData);
        var item = await db.InventoryItemHistories.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId, ct);
        if (item is not null) return new(item.ActorAccountId, item.ActionCode, item.RequestHash, item.ResponseData);
        var policy = await db.Set<InventoryNoticePolicyHistory>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId, ct);
        return policy is null ? null : new(policy.ChangedByAccountId, "UPDATE_POLICY", policy.RequestHash, policy.ResponseData);
    }

    public async Task<IReadOnlyList<Guid>> RecipientsAsync(Guid departmentId, CancellationToken ct)
        => await (from a in db.Accounts join p in db.UserProfiles on a.UserProfileId equals p.Id
            where !a.IsLock && p.DepartmentId == departmentId select a.Id).ToListAsync(ct);

    public Task<bool> OverlapsAsync(Guid slipId, Guid departmentId, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct)
        => db.InventorySlips.AnyAsync(s => s.Id != slipId && s.DepartmentId == departmentId
            && (s.Status == InventoryStates.Preparing || s.Status == InventoryStates.InProgress)
            && s.Revisions.Any(r => r.Status == InventoryStates.Published
                && r.RevisionNo == s.Revisions.Where(p => p.Status == InventoryStates.Published).Max(p => p.RevisionNo)
                && (end == null
                    ? r.PlannedStartAt == start || r.PlannedStartAt <= start && r.PlannedEndAt > start
                    : r.PlannedStartAt < end && (r.PlannedEndAt == null ? r.PlannedStartAt >= start : r.PlannedEndAt > start))), ct);

    public void ReplaceDraft(InventoryPlanRevision revision, IReadOnlyList<InventoryPlanItem> items, IReadOnlyList<InventoryRevisionAssignee> assignees)
    {
        db.RemoveRange(revision.Items); db.RemoveRange(revision.Assignees);
        revision.Items = items.ToList(); revision.Assignees = assignees.ToList();
    }

    public Task<PagedResult<InventoryHistoryDto>> HistoryAsync(Guid id, int page, int size, CancellationToken ct)
        => PageAsync(db.InventoryHistories.AsNoTracking().Where(x => x.SlipId == id).OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id)
            .Select(x => new InventoryHistoryDto(x.Id, x.TargetKey, x.ActionCode, x.ActorAccountId, x.OccurredAt, x.ReasonCode, x.BeforeData, x.AfterData)), page, size, ct);

    private static readonly Expression<Func<InventoryItemHistory, InventoryItemHistoryDto>> ItemDto = x =>
        new(x.Id, x.ResultId, x.ActionCode, x.BeforeExists, x.AfterExists, x.VersionBefore, x.VersionAfter,
            x.ActorAccountId, x.ActorNameSnapshot, x.OccurredAt, x.Reason);
    public Task<PagedResult<InventoryItemHistoryDto>> ItemHistoryAsync(Guid itemId, int page, int size, CancellationToken ct)
        => PageAsync((from h in db.InventoryItemHistories.AsNoTracking() join r in db.InventoryResults on h.ResultId equals r.Id
            where r.PlanItemId == itemId orderby h.VersionAfter descending select h).Select(ItemDto), page, size, ct);
    public async Task<IReadOnlyList<InventoryItemHistoryDto>> CorrectionsAsync(Guid revisionId, CancellationToken ct)
        => await (from h in db.InventoryItemHistories.AsNoTracking() join r in db.InventoryResults on h.ResultId equals r.Id
            join i in db.InventoryPlanItems on r.PlanItemId equals i.Id
            where i.RevisionId == revisionId && h.ActionCode == "RESULT_CORRECTED"
            orderby h.OccurredAt, h.Id select h).Select(ItemDto).ToListAsync(ct);

    public async Task<IReadOnlyList<InventorySlip>> DueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.InventorySlips.Where(s => s.Status == InventoryStates.Preparing && s.Revisions.Any(r =>
            r.Status == InventoryStates.Published && r.PlannedStartAt > now && r.PlannedStartAt <= now.AddHours(24)
            && r.RevisionNo == s.Revisions.Where(p => p.Status == InventoryStates.Published).Max(p => p.RevisionNo)))
            .Select(x => x.Id).ToListAsync(ct);
        var result = new List<InventorySlip>();
        foreach (var id in ids) if (await LoadAsync(id, false, ct) is { } slip) result.Add(slip);
        return result;
    }
    public Task<bool> NoticeExistsAsync(string key, CancellationToken ct)
        => db.InventoryNotifications.AnyAsync(x => x.EventKey == key, ct);
    public Task<PagedResult<InventoryNotificationDto>> InboxAsync(Guid accountId, int page, int size, CancellationToken ct)
        => PageAsync(db.InventoryNotifications.AsNoTracking().Where(x => x.RecipientAccountId == accountId)
            .OrderByDescending(x => x.PublishedAt).ThenBy(x => x.Id).Select(x => new InventoryNotificationDto(x.Id,
                x.SlipId, x.RevisionId, x.EventType, x.PayloadSnapshot, x.PublishedAt, x.ReadAt)), page, size, ct);
    public Task<PagedResult<InventoryPolicyHistoryDto>> PolicyHistoryAsync(int page, int size, CancellationToken ct)
        => PageAsync(db.Set<InventoryNoticePolicyHistory>().AsNoTracking().OrderByDescending(x => x.ChangedAt).ThenBy(x => x.Id)
            .Select(x => new InventoryPolicyHistoryDto(x.PreviousDays, x.NewDays, x.ChangedByAccountId, x.ChangedAt, x.Reason)), page, size, ct);
    public Task<PagedResult<InventoryDeviceOption>> DeviceOptionsAsync(Guid departmentId, string? search, int page, int size, CancellationToken ct)
    {
        var query = db.Devices.AsNoTracking().Where(d => d.DepartmentId == departmentId);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(d => d.DeviceCode.ToLower().Contains(search) || d.Name.ToLower().Contains(search)
                || (d.SerialNumber != null && d.SerialNumber.ToLower().Contains(search)));
        return PageAsync(query.OrderBy(d => d.DeviceCode).ThenBy(d => d.Id)
            .Select(d => new InventoryDeviceOption(d.Id, d.DeviceCode, d.Name, d.SerialNumber)), page, size, ct);
    }

    public Task<PagedResult<InventoryAssigneeOption>> AssigneeOptionsAsync(Guid departmentId, string? search, int page, int size, CancellationToken ct)
    {
        var query = from account in db.Accounts.AsNoTracking()
            join profile in db.UserProfiles on account.UserProfileId equals profile.Id
            join role in db.Roles on account.RoleId equals role.Id
            where profile.DepartmentId == departmentId && !account.IsLock
                && (role.Code == CSVC.Core.Accounts.RoleCodes.EquipmentManager || role.Code == CSVC.Core.Accounts.RoleCodes.EquipmentStaff)
            select new { account.Id, Code = account.AccountCode, Name = profile.FullName };
        if (!string.IsNullOrEmpty(search))
            query = query.Where(a => a.Code.ToLower().Contains(search) || a.Name.ToLower().Contains(search));
        return PageAsync(query.OrderBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new InventoryAssigneeOption(a.Id, a.Code, a.Name)), page, size, ct);
    }

    private static async Task<PagedResult<T>> PageAsync<T>(IQueryable<T> query, int page, int size, CancellationToken ct)
        => new(await query.Skip((page - 1) * size).Take(size).ToListAsync(ct), await query.CountAsync(ct), page, size);
}
