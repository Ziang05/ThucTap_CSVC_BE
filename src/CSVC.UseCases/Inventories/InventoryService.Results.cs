using System.Text;
using System.Text.Json;
using CSVC.Core.Inventories;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Inventories;

public sealed partial class InventoryService
{
    public Task<InventoryResultDto> RecordAsync(Guid id, Guid itemId, RecordInventoryResultRequest request, CancellationToken ct)
        => CommandAsync(request.RequestId, "RESULT_RECORDED", new { id, itemId, request }, false, async (actor, hash, token) =>
        {
            Validate(request); var slip = await LoadAsync(id, actor, true, token); State(slip, InventoryStates.InProgress);
            var revision = Active(slip); Assigned(revision, actor);
            var item = revision.Items.SingleOrDefault(x => x.Id == itemId) ?? throw Missing();
            if (item.Result is not null) throw Conflict("ITEM_ALREADY_CHECKED", "The item already has a result. Use the correction operation.");
            var now = clock.GetUtcNow();
            var result = new InventoryResult { PlanItemId = itemId, Exists = request.Exists!.Value,
                CheckedByAccountId = actor.Id, CheckerCodeSnapshot = actor.Code, CheckerNameSnapshot = actor.Name,
                CheckedAt = now, InitialRequestId = request.RequestId };
            item.Result = result; await results.AddAsync(result, token);
            // Flush the current result before its history trigger; both remain in the enclosing transaction.
            await uow.CommitAsync(token);
            var dto = ResultDto(result);
            await itemHistory.AddAsync(new() { ResultId = result.Id, ActionCode = "RESULT_RECORDED", RequestId = request.RequestId,
                RequestHash = hash, ResponseData = JsonSerializer.Serialize(dto), AfterExists = result.Exists,
                VersionAfter = 0, ActorAccountId = actor.Id, ActorNameSnapshot = actor.Name, OccurredAt = now }, token);
            return dto;
        }, ct);

    public Task<InventoryResultDto> CorrectAsync(Guid id, Guid itemId, CorrectInventoryResultRequest request, CancellationToken ct)
        => CommandAsync(request.RequestId, "RESULT_CORRECTED", new { id, itemId, request }, false, async (actor, hash, token) =>
        {
            Validate(request); var reason = Reason(request.Reason);
            var slip = await LoadAsync(id, actor, true, token); State(slip, InventoryStates.InProgress);
            var revision = Active(slip); Assigned(revision, actor);
            var result = revision.Items.SingleOrDefault(x => x.Id == itemId)?.Result ?? throw Missing();
            if (request.ExpectedVersion != result.Version) throw Conflict("RESULT_CHANGED", "The result changed. Reload before correcting it.");
            if (result.Exists == request.NewExists) throw Invalid("VALUE_UNCHANGED", "The new result must differ from the current result.");
            var before = result.Exists; var version = result.Version; var now = clock.GetUtcNow();
            result.Exists = request.NewExists!.Value; result.Version++;
            result.LastCorrectedByAccountId = actor.Id; result.LastCorrectedAt = now;
            await uow.CommitAsync(token);
            var dto = ResultDto(result);
            await itemHistory.AddAsync(new() { ResultId = result.Id, ActionCode = "RESULT_CORRECTED", RequestId = request.RequestId,
                RequestHash = hash, ResponseData = JsonSerializer.Serialize(dto), BeforeExists = before, AfterExists = result.Exists,
                VersionBefore = version, VersionAfter = result.Version, ActorAccountId = actor.Id, ActorNameSnapshot = actor.Name,
                OccurredAt = now, Reason = reason }, token);
            return dto;
        }, ct);

    public async Task<PagedResult<InventorySummaryDto>> ListAsync(InventoryListQuery query, CancellationToken ct)
    {
        Validate(query); Page(query.Page, query.PageSize);
        if (query.From > query.To || query.Status is { } state && state is not (InventoryStates.Pending or InventoryStates.Preparing
            or InventoryStates.InProgress or InventoryStates.Completed or InventoryStates.Canceled)) throw Invalid("INVALID_FILTER", "Invalid inventory filter.");
        var actor = await ActorAsync(ct);
        return await repo.ListAsync(query, actor.IsEquipment ? null : actor.DepartmentId, ct);
    }
    public async Task<object> GetAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); var slip = await LoadAsync(id, actor, false, ct);
        return actor.IsEquipment ? Detail(slip) : Plan(slip, Active(slip));
    }
    public async Task<InventoryPlanDto> PlanAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); var slip = await LoadAsync(id, actor, false, ct);
        return Plan(slip, Current(slip));
    }
    public async Task<InventoryProgressDto> ProgressAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); Equipment(actor); var slip = await LoadAsync(id, actor, false, ct);
        Reporting(slip); return Progress(Active(slip));
    }
    public async Task<PagedResult<InventoryHistoryDto>> HistoryAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); Equipment(actor); Page(page, pageSize);
        await LoadAsync(id, actor, false, ct); return await repo.HistoryAsync(id, page, pageSize, ct);
    }
    public async Task<PagedResult<InventoryItemHistoryDto>> ItemHistoryAsync(Guid id, Guid itemId, int page, int pageSize, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); Equipment(actor); Page(page, pageSize);
        var slip = await LoadAsync(id, actor, false, ct);
        if (!slip.Revisions.SelectMany(x => x.Items).Any(x => x.Id == itemId)) throw Missing();
        return await repo.ItemHistoryAsync(itemId, page, pageSize, ct);
    }
    public Task<InventoryReportDto> ReportAsync(Guid id, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ActorAsync(token); Equipment(actor); var slip = await LoadAsync(id, actor, false, token); Reporting(slip);
            var revision = Active(slip);
            return new InventoryReportDto(slip.Status == InventoryStates.Completed ? "OFFICIAL" : "DRAFT", Plan(slip, revision),
                Progress(revision), revision.Items.Where(x => x.Result is not null).OrderBy(x => x.DeviceCodeSnapshot)
                    .Select(x => ResultDto(x.Result!)).ToArray(), await repo.CorrectionsAsync(revision.Id, token), slip.CompletedAt, slip.CompletedByAccountId);
        }, ct);
    public async Task<byte[]> ExportAsync(Guid id, CancellationToken ct)
    {
        var report = await ReportAsync(id, ct); var csv = new StringBuilder();
        void Row(params object?[] cells) => csv.AppendLine(string.Join(',', cells.Select(x => Csv(x?.ToString() ?? ""))));
        Row("Document", report.DocumentStatus, "Slip", report.Plan.DisplayCode, "Department", report.Plan.DepartmentCode, report.Plan.DepartmentName);
        Row("Created by", report.Plan.Creator.AccountCode, report.Plan.Creator.Name, "Revision", report.Plan.RevisionNo);
        Row("Total", report.Progress.Total, "Checked", report.Progress.Checked, "Exists", report.Progress.Exists, "Not exists", report.Progress.NotExists, "Unchecked", report.Progress.Unchecked);
        Row("Device code", "Device name", "Serial", "Quantity snapshot", "Result", "Original inspector", "Original inspector name", "Checked at UTC", "Version");
        foreach (var item in report.Plan.Items)
        {
            var result = report.Results.SingleOrDefault(x => x.ItemId == item.Id);
            Row(item.DeviceCode, item.DeviceName, item.Serial, item.Quantity, result is null ? "NOT_CHECKED" : result.Exists ? "EXISTS" : "NOT_EXISTS",
                result?.CheckedBy.AccountCode, result?.CheckedBy.Name, result?.CheckedAt.ToString("O"), result?.Version);
        }
        Row("Original inspectors", "Name", "Items", "First checked at UTC");
        foreach (var inspector in report.Progress.Inspectors) Row(inspector.Person.AccountCode, inspector.Person.Name, inspector.CheckedItems, inspector.FirstCheckedAt.ToString("O"));
        Row("Corrections: result ID", "Before", "After", "Version", "Editor", "Time UTC", "Reason");
        foreach (var correction in report.Corrections) Row(correction.ResultId, correction.BeforeExists, correction.AfterExists,
            correction.VersionAfter, correction.ActorName, correction.OccurredAt.ToString("O"), correction.Reason);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
    private static string Csv(string value)
    {
        if (value.Length > 0 && ("=+-@".Contains(value.TrimStart().FirstOrDefault()) || value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private async Task<InventorySlip> LoadAsync(Guid id, Actor actor, bool tracked, CancellationToken ct)
    {
        var slip = await repo.LoadAsync(id, tracked, ct) ?? throw Missing();
        if (!actor.IsEquipment && (slip.DepartmentId != actor.DepartmentId || slip.Status != InventoryStates.Preparing
            || !slip.Revisions.Any(x => x.Status == InventoryStates.Published))) throw Missing();
        return slip;
    }
    private static InventoryPlanRevision Active(InventorySlip slip)
        => slip.Revisions.Where(x => x.Status == InventoryStates.Published).MaxBy(x => x.RevisionNo) ?? throw Conflict("PLAN_NOT_PUBLISHED", "No published plan exists.");
    private static InventoryPlanRevision Current(InventorySlip slip)
        => slip.Revisions.Any(x => x.Status == InventoryStates.Published) ? Active(slip) : slip.Revisions.MaxBy(x => x.RevisionNo)!;
    private static InventoryPersonDto Person(Guid id, string code, string name) => new(id, code, name);
    private static InventoryPlanItemDto ItemDto(InventoryPlanItem i) => new(i.Id, i.DeviceId, i.DeviceCodeSnapshot,
        i.DeviceNameSnapshot, i.SerialSnapshot, i.QuantitySnapshot, i.DepartmentIdSnapshot, i.AreaIdSnapshot, i.AreaPathSnapshot, i.CapturedAt);
    private static InventoryRevisionDto RevisionDto(InventoryPlanRevision r) => new(r.Id, r.RevisionNo, r.Status, r.PlannedStartAt,
        r.PlannedEndAt, r.Version, r.ChangeReason, r.DiffData, r.Assignees.OrderBy(x => x.AccountCodeSnapshot)
            .Select(x => Person(x.AccountId, x.AccountCodeSnapshot, x.NameSnapshot)).ToArray(),
        r.Items.OrderBy(x => x.DeviceCodeSnapshot).Select(ItemDto).ToArray());
    private static InventoryPlanDto Plan(InventorySlip s, InventoryPlanRevision r) => new(s.Id, s.DisplayCode, s.Status,
        s.DepartmentId, s.DepartmentCodeSnapshot, s.DepartmentNameSnapshot, Person(s.CreatedByAccountId, s.CreatorCodeSnapshot, s.CreatorNameSnapshot),
        s.CreatedAt, r.Id, r.RevisionNo, r.Status, r.PlannedStartAt, r.PlannedEndAt, r.PublishedAt, r.NoticeDaysSnapshot, s.Version, s.Notes,
        r.Items.OrderBy(x => x.DeviceCodeSnapshot).Select(ItemDto).ToArray());
    private static InventoryResultDto ResultDto(InventoryResult r) => new(r.PlanItemId, r.Exists,
        Person(r.CheckedByAccountId, r.CheckerCodeSnapshot, r.CheckerNameSnapshot), r.CheckedAt, r.LastCorrectedByAccountId, r.LastCorrectedAt, r.Version);
    private static InventoryProgressDto Progress(InventoryPlanRevision r)
    {
        var results = r.Items.Where(x => x.Result is not null).Select(x => x.Result!).ToArray();
        return new(r.Items.Count, results.Length, results.Count(x => x.Exists), results.Count(x => !x.Exists), r.Items.Count - results.Length,
            results.GroupBy(x => x.CheckedByAccountId).Select(x => new InventoryInspectorDto(Person(x.Key, x.First().CheckerCodeSnapshot,
                x.First().CheckerNameSnapshot), x.Count(), x.Min(v => v.CheckedAt))).OrderBy(x => x.FirstCheckedAt).ThenBy(x => x.Person.AccountId).ToArray());
    }
    private static InventorySlipDto Detail(InventorySlip slip)
    {
        var revision = Current(slip);
        return new(Plan(slip, revision), slip.Revisions.OrderBy(x => x.RevisionNo).Select(RevisionDto).ToArray(), Progress(revision),
            revision.Items.Where(x => x.Result is not null).OrderBy(x => x.DeviceCodeSnapshot).Select(x => ResultDto(x.Result!)).ToArray(),
            slip.StartedAt, slip.CompletedAt, slip.CancelReason);
    }
    private static void Reporting(InventorySlip slip)
    {
        if (slip.Status is not (InventoryStates.InProgress or InventoryStates.Completed)) throw Conflict("REPORT_NOT_STARTED", "Results are available after inventory starts.");
    }
    private static void Page(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
            throw Invalid("INVALID_PAGE", "Use a positive page and a page size of at most 100.");
    }
}
