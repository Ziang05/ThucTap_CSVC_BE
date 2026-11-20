using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.Inventories;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Inventories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class InventoryApiTests
{
    private const string Root = "/api/v1/inventory-slips";
    private static string Route(Guid id) => $"{Root}/{id}";
    private static InventoryCommand Command(int version) => new() { RequestId = Guid.NewGuid(), ExpectedVersion = version };
    private static InventoryReasonRequest Reason(int version, string reason = "Recheck plan") => new() { RequestId = Guid.NewGuid(), ExpectedVersion = version, Reason = reason };
    private static RecordInventoryResultRequest Record(bool value = true) => new() { RequestId = Guid.NewGuid(), Exists = value };
    private static CorrectInventoryResultRequest Correction(int version, bool value = false) => new() { RequestId = Guid.NewGuid(), ExpectedVersion = version, NewExists = value, Reason = "Verified again" };

    [PostgreSqlFact]
    public async Task Slip_list_filters_and_sorts_before_paginating_without_widening_department_scope()
    {
        await using var f = await Fixture.CreateAsync();
        var laterBody = f.CreateBody();
        laterBody.PlannedStartAt = laterBody.PlannedStartAt.AddDays(2);
        laterBody.PlannedEndAt = laterBody.PlannedEndAt?.AddDays(2);
        var later = await f.Create(laterBody);
        f.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        var earlier = await f.Publish(await f.Create());

        async Task<CSVC.UseCases.Common.PagedResult<InventorySummaryDto>> List(HttpClient client, string query) =>
            await Body<CSVC.UseCases.Common.PagedResult<InventorySummaryDto>>(await client.GetAsync(Root + query));
        Assert.Equal(earlier.Plan.SlipId, Assert.Single((await List(f.Staff, "?pageSize=1")).Items).Id);
        var sorted = await List(f.Staff, $"?departmentId={f.TargetId}&sortBy=PlannedStartAt&sortDirection=Desc&pageSize=1");
        Assert.Equal(2, sorted.TotalCount);
        Assert.Equal(later.Plan.SlipId, Assert.Single(sorted.Items).Id);
        var secondPage = await List(f.Staff, "?sortBy=PlannedStartAt&sortDirection=Desc&pageSize=1&page=2");
        Assert.Equal(earlier.Plan.SlipId, Assert.Single(secondPage.Items).Id);
        var bound = Uri.EscapeDataString(earlier.Plan.PlannedStartAt.ToOffset(TimeSpan.FromHours(7)).ToString("O"));
        var filtered = await List(f.Staff, $"?departmentId={f.TargetId}&status=PREPARING&from={bound}&to={bound}");
        Assert.Equal(earlier.Plan.SlipId, Assert.Single(filtered.Items).Id);
        Assert.Equal(earlier.Plan.SlipId, Assert.Single((await List(f.Pb, "?sortBy=Status&sortDirection=Asc")).Items).Id);
        Assert.Empty((await List(f.Other, $"?departmentId={f.TargetId}&sortBy=TotalItems")).Items);
        await Expect(await f.Staff.GetAsync(Root + "?sortBy=Invalid"), HttpStatusCode.BadRequest);
    }

    [PostgreSqlFact]
    public async Task Plan_options_are_scoped_and_return_usable_identifiers()
    {
        await using var f = await Fixture.CreateAsync();
        var devices = await f.Staff.GetFromJsonAsync<CSVC.UseCases.Common.PagedResult<InventoryDeviceOption>>(
            $"{Root}/options/devices?departmentId={f.TargetId}&pageSize=1");
        Assert.NotNull(devices);
        Assert.Equal(f.DeviceIds.Count, devices.TotalCount);
        Assert.Contains(Assert.Single(devices.Items).Id, f.DeviceIds);

        var people = await f.Staff.GetFromJsonAsync<CSVC.UseCases.Common.PagedResult<InventoryAssigneeOption>>(
            $"{Root}/options/assignees");
        Assert.NotNull(people);
        Assert.Contains(people.Items, item => item.Id == f.StaffId);
        Assert.DoesNotContain(people.Items, item => item.Id == f.PbId);
        await Expect(await f.Pb.GetAsync($"{Root}/options/devices?departmentId={f.TargetId}"), HttpStatusCode.Forbidden);
        await Expect(await f.Pb.GetAsync($"{Root}/options/assignees"), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Full_lifecycle_preserves_original_inspectors_replays_and_official_snapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.CreateBody();
        var slip = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Root, request), HttpStatusCode.Created);
        Assert.Matches(@"^KK-20261021-\d{4,}$", slip.Plan.DisplayCode);
        var retry = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Root, request), HttpStatusCode.Created);
        Assert.Equal(slip.Plan.SlipId, retry.Plan.SlipId);
        Assert.Equal(slip.Plan.DisplayCode, retry.Plan.DisplayCode);
        Assert.Empty(slip.Progress.Inspectors);
        var id = slip.Plan.SlipId; var route = Route(id);
        await Expect(await f.Pb.GetAsync(route), HttpStatusCode.NotFound);
        slip = await f.Publish(slip);
        var preview = await f.Pb.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal(2, preview.GetProperty("items").GetArrayLength());
        foreach (var property in new[] { "results", "progress", "revisions", "checkedBy", "inspectors" }) Assert.False(preview.TryGetProperty(property, out _));
        await Expect(await f.Other.GetAsync(route), HttpStatusCode.NotFound);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/start", Command(1)), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/start", Command(1)), HttpStatusCode.Conflict);
        f.Host.Clock.Advance(TimeSpan.FromDays(4));
        slip = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(route + "/start", Command(1)));
        Assert.Empty(slip.Progress.Inspectors);
        await Expect(await f.Pb.GetAsync(route), HttpStatusCode.NotFound);
        var item = slip.Plan.Items[0].Id; var first = Record();
        var initial = await Body<InventoryResultDto>(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", first));
        await Expect(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", Record()), HttpStatusCode.Conflict, "ITEM_ALREADY_CHECKED");
        var correction = Correction(0);
        var corrected = await Body<InventoryResultDto>(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", correction));
        Assert.Equal(initial.CheckedAt, corrected.CheckedAt); Assert.Equal(initial.CheckedBy, corrected.CheckedBy);
        Assert.Equal(f.EditorId, corrected.LastCorrectedByAccountId); Assert.Equal(1, corrected.Version);
        Assert.Equal(corrected, await Body<InventoryResultDto>(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", correction)));
        Assert.Equal(initial, await Body<InventoryResultDto>(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", first)));
        await Expect(await f.Editor.PostAsJsonAsync(route + $"/items/{item}/results", first), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        await Expect(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(0, true)), HttpStatusCode.Conflict, "RESULT_CHANGED");
        await Expect(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(1)), HttpStatusCode.BadRequest, "VALUE_UNCHANGED");
        await Expect(await f.Manager.PostAsJsonAsync(route + "/complete", Command(2)), HttpStatusCode.Conflict, "INVENTORY_HAS_UNCHECKED_ITEMS");
        var progress = await f.Manager.GetFromJsonAsync<InventoryProgressDto>(route + "/progress");
        Assert.Equal(f.StaffId, Assert.Single(progress!.Inspectors).Person.AccountId); Assert.Equal(1, progress.NotExists);
        await Body<InventoryResultDto>(await f.Editor.PostAsJsonAsync(route + $"/items/{slip.Plan.Items[1].Id}/results", Record()));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", Command(2)), HttpStatusCode.Forbidden);
        var complete = Command(2);
        slip = await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(route + "/complete", complete));
        Assert.Equal("COMPLETED", slip.Plan.Status); Assert.Equal(2, slip.Progress.Inspectors.Count);
        await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(route + "/complete", complete));
        await Expect(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(1, true)), HttpStatusCode.Conflict, "INVENTORY_COMPLETED");
        await Expect(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", Record()), HttpStatusCode.Conflict, "INVENTORY_COMPLETED");
        await Expect(await f.Manager.PostAsJsonAsync(route + "/cancel", Reason(3)), HttpStatusCode.Conflict);
        var report = await f.Manager.GetStringAsync(route + "/report");
        Assert.Contains("OFFICIAL", report); var csv = await f.Manager.GetByteArrayAsync(route + "/report.csv");
        await f.InDb(async db =>
        {
            var device = await db.Devices.FirstAsync(); device.Name = "Changed live name";
            (await db.UserProfiles.FirstAsync(x => x.FullName == "Test Staff")).FullName = "Changed live inspector";
            Assert.Equal(3, await db.InventoryItemHistories.CountAsync());
            Assert.False(await db.InventoryHistories.AnyAsync(x => x.ActionCode.StartsWith("RESULT_")));
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Empty(await db.DeviceHistories.ToListAsync());
            Assert.All(await db.Devices.ToListAsync(), d => { Assert.Equal(f.TargetId, d.DepartmentId); Assert.Equal(DeviceCondition.Good, d.Condition); });
            await db.SaveChangesAsync();
        });
        Assert.Equal(report, await f.Manager.GetStringAsync(route + "/report")); Assert.Equal(csv, await f.Manager.GetByteArrayAsync(route + "/report.csv"));
        var events = await f.Manager.GetFromJsonAsync<JsonElement>(route + $"/items/{item}/history");
        Assert.Equal(2, events.GetProperty("totalCount").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Draft_edits_ownership_empty_draft_cancellation_and_versions_are_enforced()
    {
        await using var f = await Fixture.CreateAsync();
        var body = f.CreateBody(); body.DeviceIds = []; body.AssigneeAccountIds = [];
        var slip = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Root, body), HttpStatusCode.Created);
        Assert.Empty(slip.Plan.Items);
        await Expect(await f.Manager.PostAsJsonAsync(Route(slip.Plan.SlipId) + $"/revisions/{slip.Plan.RevisionId}/publish", Command(0)), HttpStatusCode.BadRequest);
        var update = f.UpdateBody(0);
        await Expect(await f.Editor.PutAsJsonAsync(Route(slip.Plan.SlipId), update), HttpStatusCode.Forbidden);
        slip = await Body<InventorySlipDto>(await f.Staff.PutAsJsonAsync(Route(slip.Plan.SlipId), update));
        Assert.Equal(2, slip.Plan.Items.Count);
        await Expect(await f.Staff.PutAsJsonAsync(Route(slip.Plan.SlipId), f.UpdateBody(0)), HttpStatusCode.Conflict, "VERSION_CONFLICT");
        slip = await Body<InventorySlipDto>(await f.Manager.PutAsJsonAsync(Route(slip.Plan.SlipId), f.UpdateBody(1)));
        var cancel = Reason(2);
        var canceled = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Route(slip.Plan.SlipId) + "/cancel", cancel));
        Assert.Equal("CANCELED", canceled.Plan.Status);
        await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Route(slip.Plan.SlipId) + "/cancel", cancel));
        await f.InDb(async db => Assert.Empty(await db.InventoryNotifications.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Revisions_keep_published_plan_visible_until_approval_and_freeze_old_items()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Publish(await f.Create()); var id = slip.Plan.SlipId;
        var request = f.UpdateBody(1); request.PlannedStartAt = f.Host.Clock.GetUtcNow().AddDays(6); request.PlannedEndAt = null; request.Reason = "Reschedule";
        var proposed = await Body<InventorySlipDto>(await f.Editor.PostAsJsonAsync(Route(id) + "/revisions", request));
        Assert.Equal(1, (await f.Pb.GetFromJsonAsync<InventoryPlanDto>(Route(id) + "/plan"))!.RevisionNo);
        var pending = proposed.Revisions.Last();
        await Expect(await f.Staff.PostAsJsonAsync(Route(id) + $"/revisions/{pending.Id}/publish", Command(2)), HttpStatusCode.Forbidden);
        await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(Route(id) + $"/revisions/{pending.Id}/reject", Reason(2)));
        request = f.UpdateBody(3); request.PlannedStartAt = f.Host.Clock.GetUtcNow().AddDays(6); request.PlannedEndAt = null; request.Reason = "Replace assignment"; request.AssigneeAccountIds = [f.EditorId];
        proposed = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Route(id) + "/revisions", request)); pending = proposed.Revisions.Last();
        slip = await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(Route(id) + $"/revisions/{pending.Id}/publish", Command(4)));
        Assert.Equal(3, slip.Plan.RevisionNo); Assert.Equal(3, (await f.Pb.GetFromJsonAsync<InventoryPlanDto>(Route(id) + "/plan"))!.RevisionNo);
        f.Host.Clock.Advance(TimeSpan.FromDays(6));
        await Expect(await f.Staff.PostAsJsonAsync(Route(id) + "/start", Command(5)), HttpStatusCode.Forbidden);
        slip = await Body<InventorySlipDto>(await f.Editor.PostAsJsonAsync(Route(id) + "/start", Command(5)));
        var oldItem = slip.Revisions[0].Items[0].Id;
        await Expect(await f.Editor.PostAsJsonAsync(Route(id) + $"/items/{oldItem}/results", Record()), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsJsonAsync(Route(id) + $"/items/{slip.Plan.Items[0].Id}/results", Record()), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(Route(id) + "/revisions", f.UpdateBody(6)), HttpStatusCode.Conflict);
        await f.InDb(async db => Assert.Equal(2, await db.InventoryNotifications.CountAsync(x => x.EventType != "REMINDER")));
    }

    [PostgreSqlFact]
    public async Task Publication_checks_source_changes_notice_policy_recipients_and_overlap()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Create();
        await f.InDb(async db => { (await db.Devices.SingleAsync(x => x.Id == f.DeviceIds[0])).Name = "Changed before approval"; await db.SaveChangesAsync(); });
        await Expect(await f.Manager.PostAsJsonAsync(Route(slip.Plan.SlipId) + $"/revisions/{slip.Plan.RevisionId}/publish", Command(0)), HttpStatusCode.Conflict, "PLAN_SOURCE_CHANGED");
        slip = await Body<InventorySlipDto>(await f.Staff.PutAsJsonAsync(Route(slip.Plan.SlipId), f.UpdateBody(0)));
        var policy = new UpdateInventoryPolicyRequest { RequestId = Guid.NewGuid(), ExpectedVersion = 0, MinimumNoticeDays = 5, Reason = "More preparation" };
        await Expect(await f.Staff.PutAsJsonAsync("/api/v1/inventory-notice-policy", policy), HttpStatusCode.Forbidden);
        await Body<InventoryPolicyDto>(await f.Manager.PutAsJsonAsync("/api/v1/inventory-notice-policy", policy));
        await Body<InventoryPolicyDto>(await f.Manager.PutAsJsonAsync("/api/v1/inventory-notice-policy", policy));
        await Expect(await f.Manager.PostAsJsonAsync(Route(slip.Plan.SlipId) + $"/revisions/{slip.Plan.RevisionId}/publish", Command(1)), HttpStatusCode.Conflict, "NOTICE_TOO_SHORT");
        policy = new() { RequestId = Guid.NewGuid(), ExpectedVersion = 1, MinimumNoticeDays = 3, Reason = "Restore" };
        await Body<InventoryPolicyDto>(await f.Manager.PutAsJsonAsync("/api/v1/inventory-notice-policy", policy));
        await f.InDb(async db => { (await db.Accounts.SingleAsync(x => x.Id == f.PbId)).IsLock = true; await db.SaveChangesAsync(); });
        await Expect(await f.Manager.PostAsJsonAsync(Route(slip.Plan.SlipId) + $"/revisions/{slip.Plan.RevisionId}/publish", Command(1)), HttpStatusCode.Conflict, "NO_NOTICE_RECIPIENTS");
        await f.InDb(async db => { (await db.Accounts.SingleAsync(x => x.Id == f.PbId)).IsLock = false; await db.SaveChangesAsync(); });
        slip = await f.Publish(slip); Assert.Equal(3, slip.Plan.NoticeDays);
        var duplicate = await f.Create();
        await Expect(await f.Manager.PostAsJsonAsync(Route(duplicate.Plan.SlipId) + $"/revisions/{duplicate.Plan.RevisionId}/publish", Command(0)), HttpStatusCode.Conflict, "SCHEDULE_CONFLICT");
        var policyHistory = await f.Manager.GetFromJsonAsync<JsonElement>("/api/v1/inventory-notice-policy/history");
        Assert.Equal(2, policyHistory.GetProperty("totalCount").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Department_access_is_scoped_and_validation_rejects_missing_fields()
    {
        await using var f = await Fixture.CreateAsync();
        await Expect(await f.Anonymous.GetAsync(Root), HttpStatusCode.Unauthorized);
        await Expect(await f.Pb.PostAsJsonAsync(Root, f.CreateBody()), HttpStatusCode.Forbidden);
        var invalid = f.CreateBody(); invalid.DeviceIds = [f.DeviceIds[0], f.DeviceIds[0]];
        await Expect(await f.Staff.PostAsJsonAsync(Root, invalid), HttpStatusCode.BadRequest);
        invalid = f.CreateBody(); invalid.AssigneeAccountIds = [f.PbId];
        await Expect(await f.Staff.PostAsJsonAsync(Root, invalid), HttpStatusCode.BadRequest);
        var slip = await f.Publish(await f.Create()); var route = Route(slip.Plan.SlipId); var item = slip.Plan.Items[0].Id;
        foreach (var path in new[] { "/progress", "/report", "/report.csv", "/history", $"/items/{item}/history" })
            await Expect(await f.Pb.GetAsync(route + path), HttpStatusCode.Forbidden);
        await Expect(await f.Pb.PostAsJsonAsync(route + $"/items/{item}/results", Record()), HttpStatusCode.Forbidden);
        await Expect(await f.Pb.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(0)), HttpStatusCode.Forbidden);
        Assert.Equal(1, (await f.Pb.GetFromJsonAsync<JsonElement>(Root)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await f.Other.GetFromJsonAsync<JsonElement>(Root)).GetProperty("totalCount").GetInt32());
        await Expect(await f.Manager.GetAsync(Root + "?page=2147483647&pageSize=100"), HttpStatusCode.BadRequest);
        f.Host.Clock.Advance(TimeSpan.FromDays(4)); await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(route + "/start", Command(1)));
        await Expect(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", new { requestId = Guid.NewGuid() }), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", new { exists = true }), HttpStatusCode.BadRequest);
        await Body<InventoryResultDto>(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", Record()));
        await Expect(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", new { requestId = Guid.NewGuid(), newExists = false, expectedVersion = 0, reason = " " }), HttpStatusCode.BadRequest);
        await Expect(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", new { requestId = Guid.NewGuid(), newExists = false, reason = "Recheck" }), HttpStatusCode.BadRequest);
    }

    [PostgreSqlFact]
    public async Task Concurrent_first_writes_corrections_retries_and_completion_are_serialized()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Start(); var route = Route(slip.Plan.SlipId); var item = slip.Plan.Items[0].Id;
        var writes = await Task.WhenAll(f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", Record()), f.Editor.PostAsJsonAsync(route + $"/items/{item}/results", Record()));
        Assert.Single(writes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(writes, x => x.StatusCode == HttpStatusCode.Conflict);
        var corrections = await Task.WhenAll(f.Staff.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(0)), f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(0)));
        Assert.Single(corrections, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(corrections, x => x.StatusCode == HttpStatusCode.Conflict);
        var second = slip.Plan.Items[1].Id; var request = Record();
        var retries = await Task.WhenAll(f.Staff.PostAsJsonAsync(route + $"/items/{second}/results", request), f.Staff.PostAsJsonAsync(route + $"/items/{second}/results", request));
        foreach (var response in retries) await Expect(response, HttpStatusCode.OK);
        var final = await Task.WhenAll(f.Manager.PostAsJsonAsync(route + "/complete", Command(2)), f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(1, true)));
        await Expect(final[0], HttpStatusCode.OK);
        Assert.Contains(final[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        await f.InDb(async db =>
        {
            Assert.Equal(2, await db.InventoryResults.CountAsync());
            Assert.Equal(final[1].StatusCode == HttpStatusCode.OK ? 4 : 3, await db.InventoryItemHistories.CountAsync());
            Assert.Equal("COMPLETED", (await db.InventorySlips.SingleAsync()).Status);
        });
    }

    [PostgreSqlFact]
    public async Task Audit_failures_rollback_initial_results_corrections_completion_and_publication()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Create(); var route = Route(slip.Plan.SlipId);
        await f.FailTable("inventory_notifications");
        await Expect(await f.Manager.PostAsJsonAsync(route + $"/revisions/{slip.Plan.RevisionId}/publish", Command(0)), HttpStatusCode.ServiceUnavailable);
        await f.UnfailTable("inventory_notifications");
        slip = await f.Publish(slip); f.Host.Clock.Advance(TimeSpan.FromDays(4));
        slip = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(route + "/start", Command(1)));
        var item = slip.Plan.Items[0].Id; await f.FailTable("inventory_item_history");
        await Expect(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", Record()), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => Assert.Empty(await db.InventoryResults.ToListAsync()));
        await f.UnfailTable("inventory_item_history");
        await Body<InventoryResultDto>(await f.Staff.PostAsJsonAsync(route + $"/items/{item}/results", Record()));
        await f.FailTable("inventory_item_history");
        await Expect(await f.Editor.PatchAsJsonAsync(route + $"/items/{item}/result", Correction(0)), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { var result = await db.InventoryResults.SingleAsync(); Assert.True(result.Exists); Assert.Equal(0, result.Version); });
        await f.UnfailTable("inventory_item_history");
        await Body<InventoryResultDto>(await f.Editor.PostAsJsonAsync(route + $"/items/{slip.Plan.Items[1].Id}/results", Record()));
        await f.FailTable("inventory_history");
        await Expect(await f.Manager.PostAsJsonAsync(route + "/complete", Command(2)), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => Assert.Equal("IN_PROGRESS", (await db.InventorySlips.SingleAsync()).Status));
    }

    [PostgreSqlFact]
    public async Task Database_guards_block_missing_history_reassignment_and_changes_after_completion()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Start(); var route = Route(slip.Plan.SlipId);
        foreach (var item in slip.Plan.Items) await Body<InventoryResultDto>(await f.Staff.PostAsJsonAsync(route + $"/items/{item.Id}/results", Record()));
        await f.RejectSql("UPDATE inventory_results SET \"exists\"=false, version=version+1,last_corrected_by_account_id=checked_by_account_id,last_corrected_at=checked_at");
        await f.RejectSql("UPDATE inventory_results SET checked_by_account_id='" + f.EditorId + "', \"exists\"=false,version=version+1,last_corrected_by_account_id=checked_by_account_id,last_corrected_at=checked_at");
        await f.RejectSql("UPDATE inventory_plan_items SET device_name_snapshot='tampered'");
        await f.RejectSql("DELETE FROM inventory_revision_assignees");
        await f.RejectSql("UPDATE inventory_item_history SET reason='tampered'");
        await f.RejectSql("DELETE FROM inventory_history");
        await f.RejectSql("UPDATE inventory_results SET \"exists\"=false,version=version+1,last_corrected_by_account_id='" + f.PbId + "',last_corrected_at=checked_at");
        await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(route + "/complete", Command(2)));
        await f.RejectSql("UPDATE inventory_slips SET status='IN_PROGRESS',version=version+1");
        await f.RejectSql("DELETE FROM inventory_results");
        await f.RejectSql("UPDATE inventory_results SET \"exists\"=false,version=version+1,last_corrected_by_account_id=checked_by_account_id,last_corrected_at=checked_at");
        await f.RejectSql("INSERT INTO inventory_plan_revisions(id,slip_id,revision_no,status,planned_start_at,proposed_by_account_id,proposed_at,version,change_reason) SELECT gen_random_uuid(),slip_id,99,'DRAFT',planned_start_at,proposed_by_account_id,proposed_at,0,'After completion' FROM inventory_plan_revisions LIMIT 1");
        await f.InDb(async db => { Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); });
    }

    [PostgreSqlFact]
    public async Task Reminders_read_marking_and_published_cancellation_are_idempotent()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Publish(await f.Create()); var route = Route(slip.Plan.SlipId);
        f.Host.Clock.Advance(TimeSpan.FromDays(3));
        await using (var scope = f.Host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            Assert.Equal(1, await service.RemindAsync(default)); Assert.Equal(0, await service.RemindAsync(default));
        }
        var inbox = await f.Pb.GetFromJsonAsync<JsonElement>("/api/v1/inventory-notifications");
        Assert.Equal(2, inbox.GetProperty("totalCount").GetInt32());
        var notice = inbox.GetProperty("items")[0].GetProperty("id").GetGuid();
        await Expect(await f.Other.PostAsync($"/api/v1/inventory-notifications/{notice}/read", null), HttpStatusCode.NotFound);
        await Expect(await f.Pb.PostAsync($"/api/v1/inventory-notifications/{notice}/read", null), HttpStatusCode.NoContent);
        await Expect(await f.Pb.PostAsync($"/api/v1/inventory-notifications/{notice}/read", null), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/cancel", Reason(1)), HttpStatusCode.Forbidden);
        var cancel = Reason(1);
        await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(route + "/cancel", cancel));
        await Body<InventorySlipDto>(await f.Manager.PostAsJsonAsync(route + "/cancel", cancel));
        await f.InDb(async db => { Assert.Equal(3, await db.InventoryNotifications.CountAsync()); Assert.Single(await db.InventoryNotifications.Where(x => x.EventType == "PLAN_CANCELED").ToListAsync()); });
        await Expect(await f.Pb.GetAsync(route + "/plan"), HttpStatusCode.NotFound);
    }

    [PostgreSqlFact]
    public async Task Exact_notice_boundary_is_valid_and_later_policy_changes_do_not_apply_retroactively()
    {
        await using var f = await Fixture.CreateAsync();
        var body = f.CreateBody(); body.PlannedStartAt = f.Host.Clock.GetUtcNow().AddHours(72).AddSeconds(-1); body.PlannedEndAt = null;
        var slip = await f.Create(body);
        await Expect(await f.Manager.PostAsJsonAsync(Route(slip.Plan.SlipId) + $"/revisions/{slip.Plan.RevisionId}/publish", Command(0)), HttpStatusCode.Conflict, "NOTICE_TOO_SHORT");
        var update = f.UpdateBody(0); update.PlannedStartAt = f.Host.Clock.GetUtcNow().AddHours(72); update.PlannedEndAt = null;
        slip = await Body<InventorySlipDto>(await f.Staff.PutAsJsonAsync(Route(slip.Plan.SlipId), update));
        slip = await f.Publish(slip); Assert.Equal(3, slip.Plan.NoticeDays);
        await Body<InventoryPolicyDto>(await f.Manager.PutAsJsonAsync("/api/v1/inventory-notice-policy", new UpdateInventoryPolicyRequest
        { RequestId = Guid.NewGuid(), ExpectedVersion = 0, MinimumNoticeDays = 5, Reason = "Applies to future publications" }));
        f.Host.Clock.Advance(TimeSpan.FromHours(72));
        slip = await Body<InventorySlipDto>(await f.Staff.PostAsJsonAsync(Route(slip.Plan.SlipId) + "/start", Command(2)));
        Assert.Equal("IN_PROGRESS", slip.Plan.Status); Assert.Equal(3, slip.Plan.NoticeDays);
    }

    [PostgreSqlFact]
    public async Task Inventory_uses_frozen_plan_even_if_live_device_moves_or_becomes_unavailable()
    {
        await using var f = await Fixture.CreateAsync(); var slip = await f.Start(); var route = Route(slip.Plan.SlipId);
        await f.InDb(async db =>
        {
            var device = await db.Devices.SingleAsync(x => x.Id == f.DeviceIds[0]);
            device.DepartmentId = (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId!.Value;
            device.Condition = DeviceCondition.Missing; device.Quantity = 0;
            await db.SaveChangesAsync();
        });
        var item = slip.Plan.Items.Single(x => x.DeviceId == f.DeviceIds[0]);
        await Body<InventoryResultDto>(await f.Staff.PostAsJsonAsync(route + $"/items/{item.Id}/results", Record(false)));
        var detail = (await f.Manager.GetFromJsonAsync<InventorySlipDto>(route))!;
        Assert.Equal(f.TargetId, detail.Plan.Items.Single(x => x.Id == item.Id).DepartmentId);
        await f.InDb(async db =>
        {
            var device = await db.Devices.SingleAsync(x => x.Id == item.DeviceId);
            Assert.NotEqual(f.TargetId, device.DepartmentId); Assert.Equal(DeviceCondition.Missing, device.Condition); Assert.Equal(0, device.Quantity);
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Empty(await db.DeviceHistories.ToListAsync());
        });
    }

    private static async Task<T> Body<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    { await Expect(response, status); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {text}");
        if (code is not null) Assert.Contains(code, text);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required AccountApiTestHost Host { get; init; }
        public required NpgsqlConnection Admin { get; init; }
        public required string Database { get; init; }
        public required string ConnectionString { get; init; }
        public required HttpClient Manager { get; init; }
        public required HttpClient Staff { get; init; }
        public required HttpClient Editor { get; init; }
        public required HttpClient Pb { get; init; }
        public required HttpClient Other { get; init; }
        public required HttpClient Anonymous { get; init; }
        public Guid StaffId { get; init; }
        public Guid EditorId { get; init; }
        public Guid PbId { get; init; }
        public Guid TargetId { get; init; }
        public required List<Guid> DeviceIds { get; init; }
        public static async Task<Fixture> CreateAsync()
        {
            var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("CSVC_POSTGRES_TEST_CONNECTION")); await admin.OpenAsync();
            var database = "csvc_inventory_" + Guid.NewGuid().ToString("N");
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
            var host = new AccountApiTestHost(connection);
            try
            {
                var seed = await host.SeedAsync();
                var target = new Department { Code = "PB_INVENTORY", Name = "Inventory target" };
                var foreign = new Department { Code = "PB_OTHER", Name = "Other department" };
                var editorProfile = new UserProfile { DepartmentId = seed.Department, FullName = "Editor C" };
                var pbProfile = new UserProfile { DepartmentId = target.Id, FullName = "Department user" };
                var otherProfile = new UserProfile { DepartmentId = foreign.Id, FullName = "Other user" };
                Account Account(string code, short role, UserProfile p) => new() { AccountCode = code, RoleId = role, UserProfileId = p.Id, Email = code + "@example.com", PasswordHash = "unused" };
                var editor = Account("TK_EDITOR", 2, editorProfile); var pb = Account("TK_PB", 3, pbProfile); var other = Account("TK_OTHER", 3, otherProfile);
                string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var et = Token(); var pt = Token(); var ot = Token();
                var category = new DeviceCategory { DisplayCode = "DM_INV", Name = "Inventory devices" };
                var devices = Enumerable.Range(1, 2).Select(i => new Device { DeviceCode = "INV-" + i, Barcode = "INV-BAR-" + i, Name = "Inventory device " + i, DepartmentId = target.Id, CategoryId = category.Id, Quantity = 1 }).ToList();
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                    db.AddRange(target, foreign, editorProfile, pbProfile, otherProfile, editor, pb, other, category); db.AddRange(devices);
                    db.AddRange(devices.Select(d => new DeviceIdentity { DeviceCode = d.DeviceCode, Barcode = d.Barcode, ReservedAt = host.Clock.GetUtcNow() }));
                    db.AddRange(AccountApiTestHost.Session(editor.Id, et), AccountApiTestHost.Session(pb.Id, pt), AccountApiTestHost.Session(other.Id, ot));
                    (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId = seed.Department;
                    foreach (var session in await db.AuthSessions.ToListAsync()) session.ExpiresAt = host.Clock.GetUtcNow().AddDays(100);
                    foreach (var entry in db.ChangeTracker.Entries<CSVC.Core.Authentication.AuthSession>()) entry.Entity.ExpiresAt = host.Clock.GetUtcNow().AddDays(100);
                    await db.SaveChangesAsync();
                }
                HttpClient Client(string? token) { var c = host.CreateClient(); if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return c; }
                return new() { Host = host, Admin = admin, Database = database, ConnectionString = connection, Manager = Client(seed.ManagerToken),
                    Staff = Client(seed.StaffToken), Editor = Client(et), Pb = Client(pt), Other = Client(ot), Anonymous = Client(null),
                    StaffId = seed.Staff, EditorId = editor.Id, PbId = pb.Id, TargetId = target.Id, DeviceIds = devices.Select(x => x.Id).ToList() };
            }
            catch { await host.DisposeAsync(); await Drop(admin, database); await admin.DisposeAsync(); throw; }
        }
        public CreateInventoryRequest CreateBody() => new() { RequestId = Guid.NewGuid(), DepartmentId = TargetId,
            PlannedStartAt = Host.Clock.GetUtcNow().AddDays(4), PlannedEndAt = Host.Clock.GetUtcNow().AddDays(4).AddHours(2),
            DeviceIds = DeviceIds, AssigneeAccountIds = [StaffId, EditorId] };
        public UpdateInventoryPlanRequest UpdateBody(int version) => new() { RequestId = Guid.NewGuid(), ExpectedVersion = version, DepartmentId = TargetId,
            PlannedStartAt = Host.Clock.GetUtcNow().AddDays(4), PlannedEndAt = Host.Clock.GetUtcNow().AddDays(4).AddHours(2),
            DeviceIds = DeviceIds, AssigneeAccountIds = [StaffId, EditorId] };
        public Task<InventorySlipDto> Create() => Create(CreateBody());
        public async Task<InventorySlipDto> Create(CreateInventoryRequest request) => await Body<InventorySlipDto>(await Staff.PostAsJsonAsync(Root, request), HttpStatusCode.Created);
        public async Task<InventorySlipDto> Publish(InventorySlipDto slip) => await Body<InventorySlipDto>(await Manager.PostAsJsonAsync(Route(slip.Plan.SlipId) + $"/revisions/{slip.Plan.RevisionId}/publish", Command(slip.Plan.Version)));
        public async Task<InventorySlipDto> Start()
        { var slip = await Publish(await Create()); Host.Clock.Advance(TimeSpan.FromDays(4)); return await Body<InventorySlipDto>(await Staff.PostAsJsonAsync(Route(slip.Plan.SlipId) + "/start", Command(1))); }
        public async Task InDb(Func<CsvcDbContext, Task> action)
        { await using var scope = Host.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>()); }
        public async Task Sql(string sql)
        { await using var c = new NpgsqlConnection(ConnectionString); await c.OpenAsync(); await using var command = new NpgsqlCommand(sql, c); await command.ExecuteNonQueryAsync(); }
        public async Task RejectSql(string sql) => await Assert.ThrowsAsync<PostgresException>(() => Sql(sql));
        public Task FailTable(string table) => Sql($"CREATE OR REPLACE FUNCTION inv_test_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'SIMULATED_AUDIT_FAILURE'; END $$; CREATE TRIGGER inv_test_fail BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION inv_test_fail()");
        public Task UnfailTable(string table) => Sql($"DROP TRIGGER inv_test_fail ON {table}");
        public async ValueTask DisposeAsync()
        {
            Manager.Dispose(); Staff.Dispose(); Editor.Dispose(); Pb.Dispose(); Other.Dispose(); Anonymous.Dispose();
            await Host.DisposeAsync(); await Drop(Admin, Database); await Admin.DisposeAsync();
        }
        private static async Task Drop(NpgsqlConnection admin, string name)
        { await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync(); }
    }
}
