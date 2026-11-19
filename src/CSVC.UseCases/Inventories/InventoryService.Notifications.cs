using System.Text.Json;
using CSVC.Core.Inventories;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Inventories;

public sealed partial class InventoryService
{
    public async Task<InventoryPolicyDto> PolicyAsync(CancellationToken ct)
    {
        Equipment(await ActorAsync(ct)); return PolicyDto(await PolicyEntityAsync(false, ct));
    }
    public Task<InventoryPolicyDto> UpdatePolicyAsync(UpdateInventoryPolicyRequest request, CancellationToken ct)
        => CommandAsync(request.RequestId, "UPDATE_POLICY", request, true, async (actor, hash, token) =>
        {
            var reason = Reason(request.Reason); var policy = await PolicyEntityAsync(true, token);
            Version(policy.Version, request.ExpectedVersion);
            if (policy.MinimumNoticeDays == request.MinimumNoticeDays) throw Invalid("VALUE_UNCHANGED", "The notice period is unchanged.");
            var previous = policy.MinimumNoticeDays; policy.MinimumNoticeDays = request.MinimumNoticeDays;
            policy.Version++; policy.UpdatedByAccountId = actor.Id; policy.UpdatedAt = clock.GetUtcNow();
            var dto = PolicyDto(policy);
            await policyHistory.AddAsync(new() { RequestId = request.RequestId, RequestHash = hash, ResponseData = JsonSerializer.Serialize(dto),
                PreviousDays = previous, NewDays = policy.MinimumNoticeDays, ChangedByAccountId = actor.Id,
                ChangedAt = policy.UpdatedAt, Reason = reason }, token);
            return dto;
        }, ct);
    public async Task<PagedResult<InventoryPolicyHistoryDto>> PolicyHistoryAsync(int page, int pageSize, CancellationToken ct)
    {
        Manager(await ActorAsync(ct)); Page(page, pageSize); return await repo.PolicyHistoryAsync(page, pageSize, ct);
    }
    public async Task<PagedResult<InventoryNotificationDto>> NotificationsAsync(int page, int pageSize, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); Page(page, pageSize); return await repo.InboxAsync(actor.Id, page, pageSize, ct);
    }
    public async Task MarkReadAsync(Guid id, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var notice = await notifications.FirstOrDefaultTrackedAsync(x => x.Id == id && x.RecipientAccountId == actor.Id, token) ?? throw Missing();
            notice.ReadAt ??= clock.GetUtcNow(); return true;
        }, ct);
    public Task<int> RemindAsync(CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var now = clock.GetUtcNow(); var count = 0;
            foreach (var slip in await repo.DueAsync(now, token)) count += await NotifyAsync(slip, Active(slip), "REMINDER", now, false, token);
            return count;
        }, ct);
    private async Task<int> NotifyAsync(InventorySlip slip, InventoryPlanRevision revision, string type,
        DateTimeOffset now, bool requireRecipients, CancellationToken ct)
    {
        var recipients = await repo.RecipientsAsync(slip.DepartmentId, ct);
        if (requireRecipients && recipients.Count == 0) throw Conflict("NO_NOTICE_RECIPIENTS", "No active account can receive this department's published plan.");
        var count = 0;
        foreach (var recipient in recipients)
        {
            var key = $"{slip.Id:N}:{revision.Id:N}:{recipient:N}:{type}";
            if (await repo.NoticeExistsAsync(key, ct)) continue;
            await notifications.AddAsync(new() { EventKey = key, SlipId = slip.Id, RevisionId = revision.Id,
                RecipientAccountId = recipient, EventType = type, PublishedAt = now, SentAt = now,
                PayloadSnapshot = JsonSerializer.Serialize(new { slip.DisplayCode, slip.DepartmentCodeSnapshot,
                    slip.DepartmentNameSnapshot, revision.RevisionNo, revision.PlannedStartAt, revision.PlannedEndAt,
                    totalItems = revision.Items.Count, planUrl = $"/api/v1/inventory-slips/{slip.Id}/plan" }) }, ct);
            count++;
        }
        return count;
    }
    private async Task<InventoryNoticePolicy> PolicyEntityAsync(bool tracked, CancellationToken ct)
        => (tracked ? await policies.FirstOrDefaultTrackedAsync(x => x.Id == 1, ct) : await policies.FirstOrDefaultAsync(x => x.Id == 1, ct))
            ?? throw Conflict("NOTICE_POLICY_MISSING", "Apply the inventory migration before using this module.");
    private static InventoryPolicyDto PolicyDto(InventoryNoticePolicy policy)
        => new(policy.MinimumNoticeDays, policy.Version, policy.UpdatedByAccountId, policy.UpdatedAt);
}
