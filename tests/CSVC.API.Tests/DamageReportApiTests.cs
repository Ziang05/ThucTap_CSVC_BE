using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DamageReports;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.DamageReports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class DamageReportApiTests
{
    private const string Url = "/api/v1/damage-reports";
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";
    private static object Image(string filename = "test.png") => new { filename, dataBase64 = Png };

    [PostgreSqlTheory]
    [InlineData("DAMAGE")]
    [InlineData("ABNORMAL")]
    [InlineData("MISSING")]
    public async Task Create_has_atomic_context_pending_history_and_no_device_side_effects(string incident)
    {
        await using var f = await Fixture.CreateAsync();
        var body = f.CreateBody("TB-A", incident: incident);
        var report = await f.Create(f.Employee, body);
        Assert.Equal(incident, report.GetProperty("incidentType").GetString());
        Assert.Equal("NOT_RECEIVED", report.GetProperty("receptionStatus").GetString());
        Assert.Equal("Department A", report.GetProperty("reportingDepartment").GetString());
        Assert.Equal(report.GetProperty("reportedAt").GetString(), report.GetProperty("detectedAt").GetString());
        Assert.Empty(report.GetProperty("evidence").EnumerateArray());
        Assert.False(report.TryGetProperty("id", out _));
        await f.Db(async db =>
        {
            Assert.Single(await db.DamageReports.ToListAsync());
            Assert.Single(await db.Set<DamageReportContext>().ToListAsync());
            Assert.Single(await db.Set<DamageReportPendingDevice>().ToListAsync());
            Assert.Empty(await db.Set<DamageReportReception>().ToListAsync());
            Assert.Single(await db.Set<DamageReportHistory>().ToListAsync());
            Assert.Empty(await db.ServiceOrders.ToListAsync());
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-A");
            Assert.Equal(DeviceCondition.Good, device.Condition); Assert.Equal(1, device.Version);
        });
        var options = await Get(f.Employee, Url + "/devices");
        Assert.False(options.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("deviceCode").GetString() == "TB-A").GetProperty("canReport").GetBoolean());
    }

    [PostgreSqlFact]
    public async Task Department_scope_uses_reporting_department_and_current_profile_in_every_read()
    {
        await using var f = await Fixture.CreateAsync();
        // QTTB can report a device used by another department; ownership remains the reporter's department.
        var own = await f.Create(f.Manager, f.CreateBody("TB-A"));
        var route = Route(own);
        Assert.Equal("Test department", own.GetProperty("reportingDepartment").GetString());
        Assert.Equal("Department A", own.GetProperty("deviceDepartment").GetString());
        await Expect(await f.Employee.GetAsync(route), HttpStatusCode.NotFound);
        await Expect(await f.Employee.GetAsync(route + "/history"), HttpStatusCode.NotFound);
        Assert.Equal(0, (await Get(f.Employee, Url)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await Get(f.Employee, Url + "?departmentCode=PB_TEST")).GetProperty("totalCount").GetInt32());
        var other = await f.Create(f.Employee, f.CreateBody("TB-B"));
        await Expect(await f.Peer.PutAsJsonAsync(Route(other), new { requestId = Guid.NewGuid(), expectedVersion = 1, incidentDescription = "Peer edit" }), HttpStatusCode.Forbidden);
        await f.Db(async db =>
        {
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-B");
            device.DepartmentId = f.EquipmentDepartment; await db.SaveChangesAsync();
        });
        Assert.Equal("Department A", (await Get(f.Employee, Route(other))).GetProperty("deviceDepartment").GetString());
        await f.Db(async db =>
        {
            var account = await db.Accounts.SingleAsync(x => x.Id == f.EmployeeId);
            (await db.UserProfiles.SingleAsync(x => x.Id == account.UserProfileId)).DepartmentId = f.EquipmentDepartment;
            await db.SaveChangesAsync();
        });
        await Expect(await f.Employee.GetAsync(Route(other)), HttpStatusCode.NotFound);
        await Expect(await f.Employee.PutAsJsonAsync(Route(other), new { requestId = Guid.NewGuid(), expectedVersion = 1, notes = "outside" }), HttpStatusCode.NotFound);
        Assert.Equal(1, (await Get(f.Employee, Url)).GetProperty("totalCount").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Create_retry_binds_actor_payload_and_result_even_after_later_edits()
    {
        await using var f = await Fixture.CreateAsync();
        var id = Guid.NewGuid(); var body = f.CreateBody("TB-A", id);
        var first = await f.Create(f.Employee, body);
        var update = new { requestId = Guid.NewGuid(), expectedVersion = 1, incidentDescription = "Updated" };
        await Expect(await f.Employee.PutAsJsonAsync(Route(first), update), HttpStatusCode.OK);
        var retry = await f.Create(f.Employee, body);
        Assert.Equal(first.GetRawText(), retry.GetRawText());
        await Expect(await f.Employee.PostAsJsonAsync(Url, f.CreateBody("TB-B", id)), HttpStatusCode.Conflict);
        await Expect(await f.Peer.PostAsJsonAsync(Url, body), HttpStatusCode.Conflict);
        await f.Db(async db => Assert.Equal(2, await db.Set<DamageReportHistory>().CountAsync()));
    }

    [PostgreSqlFact]
    public async Task Concurrent_creates_allow_one_pending_and_same_key_retries_return_one_result()
    {
        await using var f = await Fixture.CreateAsync();
        var races = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => f.Employee.PostAsJsonAsync(Url, f.CreateBody("TB-A"))));
        Assert.Single(races, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Equal(4, races.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var same = f.CreateBody("TB-B");
        var retries = await Task.WhenAll(f.Employee.PostAsJsonAsync(Url, same), f.Employee.PostAsJsonAsync(Url, same));
        foreach (var response in retries) await Expect(response, HttpStatusCode.Created);
        Assert.Equal(await retries[0].Content.ReadAsStringAsync(), await retries[1].Content.ReadAsStringAsync());
        await f.Db(async db =>
        {
            Assert.Equal(2, await db.DamageReports.CountAsync());
            Assert.Equal(2, await db.Set<DamageReportPendingDevice>().CountAsync());
            Assert.Equal(2, await db.Set<DamageReportHistory>().CountAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Reception_is_atomic_immutable_idempotent_and_does_not_release_a_new_reports_pending()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.Create(f.Employee, f.CreateBody("TB-A"));
        var request = new { requestId = Guid.NewGuid(), expectedVersion = 1 };
        await Expect(await f.Employee.PostAsJsonAsync(Route(first) + "/receive", request), HttpStatusCode.Forbidden);
        var received = await Json(await f.Staff.PostAsJsonAsync(Route(first) + "/receive", request), HttpStatusCode.OK);
        Assert.Equal("TK_STAFF", received.GetProperty("receivedByAccountCode").GetString());
        Assert.Equal("RECEIVED", received.GetProperty("receptionStatus").GetString());
        var second = await f.Create(f.Employee, f.CreateBody("TB-A"));
        var retry = await Json(await f.Staff.PostAsJsonAsync(Route(first) + "/receive", request), HttpStatusCode.OK);
        Assert.Equal(received.GetRawText(), retry.GetRawText());
        await Expect(await f.Manager.PostAsJsonAsync(Route(first) + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = 2 }), HttpStatusCode.Conflict);
        await f.Db(async db =>
        {
            Assert.Single(await db.Set<DamageReportPendingDevice>().ToListAsync());
            Assert.Single(await db.Set<DamageReportReception>().ToListAsync());
            Assert.Equal(3, await db.Set<DamageReportHistory>().CountAsync());
            Assert.Equal((await db.DamageReports.SingleAsync(x => x.ReportCode == second.GetProperty("reportCode").GetString())).Id,
                (await db.Set<DamageReportPendingDevice>().SingleAsync()).DamageReportId);
        });
    }

    [PostgreSqlFact]
    public async Task Updates_use_versions_and_idempotency_and_no_change_does_not_add_audit()
    {
        await using var f = await Fixture.CreateAsync();
        var r = await f.Create(f.Employee, f.CreateBody("TB-A")); var route = Route(r);
        var noop = new { requestId = Guid.NewGuid(), expectedVersion = 1 };
        var unchanged = await Json(await f.Employee.PutAsJsonAsync(route, noop), HttpStatusCode.OK);
        Assert.Equal(1, unchanged.GetProperty("rowVersion").GetInt32());
        var edit = new { requestId = Guid.NewGuid(), expectedVersion = 1, notes = " New note ", incidentType = "MISSING" };
        var changed = await Json(await f.Employee.PutAsJsonAsync(route, edit), HttpStatusCode.OK);
        Assert.Equal(2, changed.GetProperty("rowVersion").GetInt32()); Assert.Equal("New note", changed.GetProperty("notes").GetString());
        var replay = await Json(await f.Employee.PutAsJsonAsync(route, edit), HttpStatusCode.OK);
        Assert.Equal(changed.GetRawText(), replay.GetRawText());
        await Expect(await f.Employee.PutAsJsonAsync(route, new { requestId = edit.requestId, expectedVersion = 1, notes = "Different" }), HttpStatusCode.Conflict);
        await Expect(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 1, notes = "Stale" }), HttpStatusCode.Conflict);
        var oldNoop = await Json(await f.Employee.PutAsJsonAsync(route, noop), HttpStatusCode.OK);
        Assert.Equal(1, oldNoop.GetProperty("rowVersion").GetInt32());
        await Expect(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 2, notes = (string?)null }), HttpStatusCode.OK);
        await f.Db(async db =>
        {
            Assert.Equal(3, await db.Set<DamageReportHistory>().CountAsync());
            Assert.Null((await db.DamageReports.SingleAsync()).Notes);
        });
    }

    [PostgreSqlFact]
    public async Task Device_change_refreshes_only_device_context_and_is_blocked_after_reception()
    {
        await using var f = await Fixture.CreateAsync();
        var r = await f.Create(f.Employee, f.CreateBody("TB-A")); var route = Route(r);
        var updated = await Json(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 1, deviceCode = "TB-B" }), HttpStatusCode.OK);
        Assert.Equal("TB-B", updated.GetProperty("deviceCode").GetString());
        Assert.Equal(r.GetProperty("reporterName").GetString(), updated.GetProperty("reporterName").GetString());
        Assert.Equal(r.GetProperty("reportedAt").GetString(), updated.GetProperty("reportedAt").GetString());
        await f.Create(f.Employee, f.CreateBody("TB-A"));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = 2 }), HttpStatusCode.OK);
        await Expect(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 3, deviceCode = "TB-C" }), HttpStatusCode.Conflict);
        var content = await Json(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 3, incidentDescription = "After reception", incidentType = "ABNORMAL" }), HttpStatusCode.OK);
        Assert.Equal("RECEIVED", content.GetProperty("receptionStatus").GetString());
        var history = await Get(f.Employee, route + "/history");
        Assert.Contains(history.GetProperty("items").EnumerateArray(), x => x.GetProperty("beforeData").GetString()?.Contains("TB-A") == true && x.GetProperty("afterData").GetString()?.Contains("TB-B") == true);
    }

    [PostgreSqlFact]
    public async Task Competing_device_changes_preserve_losers_pending_and_reception_race_has_one_winner()
    {
        await using var f = await Fixture.CreateAsync();
        var a = await f.Create(f.Employee, f.CreateBody("TB-A")); var b = await f.Create(f.Employee, f.CreateBody("TB-B"));
        var results = await Task.WhenAll(new[] { a, b }.Select(r => f.Employee.PutAsJsonAsync(Route(r), new { requestId = Guid.NewGuid(), expectedVersion = 1, deviceCode = "TB-C" })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await f.Db(async db => Assert.Equal(2, await db.Set<DamageReportPendingDevice>().CountAsync()));
        var current = await Get(f.Employee, Route(a)); var version = current.GetProperty("rowVersion").GetInt32();
        var race = await Task.WhenAll(f.Employee.PutAsJsonAsync(Route(a), new { requestId = Guid.NewGuid(), expectedVersion = version, incidentDescription = "race" }),
            f.Staff.PostAsJsonAsync(Route(a) + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = version }));
        Assert.Single(race, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Evidence_can_add_replace_remove_after_reception_with_private_authorized_reads()
    {
        await using var f = await Fixture.CreateAsync();
        var r = await f.Create(f.Employee, new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "With image", images = new[] { Image() } });
        var route = Route(r); var content = r.GetProperty("evidence")[0].GetProperty("contentUrl").GetString()!;
        var download = await f.Employee.GetAsync(content); await Expect(download, HttpStatusCode.OK);
        Assert.Equal(Convert.FromBase64String(Png), await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("no-store, private", string.Join(", ", download.Headers.CacheControl!.ToString().Split(", ").Order()));
        await Expect(await f.Outsider.GetAsync(content), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = 1 }), HttpStatusCode.OK);
        var replace = new { requestId = Guid.NewGuid(), expectedVersion = 2, replaceImages = new[] { new { sequenceNo = 1, image = Image("replacement.png") } } };
        var updated = await Json(await f.Employee.PutAsJsonAsync(route, replace), HttpStatusCode.OK);
        Assert.Equal(2, updated.GetProperty("evidence")[0].GetProperty("sequenceNo").GetInt32());
        await Expect(await f.Employee.GetAsync(content), HttpStatusCode.NotFound);
        await Expect(await f.Employee.PutAsJsonAsync(route, replace), HttpStatusCode.OK);
        await Expect(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 3, removeImages = new[] { 2 } }), HttpStatusCode.OK);
        var add = await Json(await f.Employee.PutAsJsonAsync(route, new { requestId = Guid.NewGuid(), expectedVersion = 4, addImages = new[] { Image() } }), HttpStatusCode.OK);
        Assert.Equal(3, add.GetProperty("evidence")[0].GetProperty("sequenceNo").GetInt32());
        await f.Db(async db =>
        {
            Assert.Equal(3, await db.Set<DamageReportEvidence>().CountAsync());
            Assert.Equal(2, await db.Set<DamageReportEvidence>().CountAsync(x => x.RemovedAt != null));
            Assert.Single(await db.Set<DamageReportHistory>().Where(x => x.ActionCode == "EVIDENCE_REPLACED").ToListAsync());
        });
        var history = await Get(f.Employee, route + "/history");
        Assert.DoesNotContain("StorageObjectKey", history.GetRawText()); Assert.DoesNotContain("DataBase64", history.GetRawText());
    }

    [PostgreSqlTheory]
    [InlineData("DAMAGE_REPORT_CREATED")]
    [InlineData("DAMAGE_REPORT_RECEIVED")]
    [InlineData("DAMAGE_REPORT_UPDATED")]
    [InlineData("EVIDENCE_ADDED")]
    public async Task Audit_failure_rolls_back_all_database_changes_and_retry_succeeds(string action)
    {
        await using var f = await Fixture.CreateAsync();
        if (action == "DAMAGE_REPORT_CREATED")
        {
            var body = f.CreateBody("TB-A"); f.Failures.Action = action;
            await Expect(await f.Employee.PostAsJsonAsync(Url, body), HttpStatusCode.ServiceUnavailable);
            await f.Db(async db => { Assert.Empty(await db.DamageReports.ToListAsync()); Assert.Empty(await db.Set<DamageReportRequest>().ToListAsync()); });
            await f.Create(f.Employee, body); return;
        }
        var r = await f.Create(f.Employee, f.CreateBody("TB-A")); var route = Route(r);
        f.Failures.Action = action;
        Func<Task<HttpResponseMessage>> operation = action switch
        {
            "DAMAGE_REPORT_RECEIVED" => () => f.Staff.PostAsJsonAsync(route + "/receive", new { requestId = f.RetryId, expectedVersion = 1 }),
            "DAMAGE_REPORT_UPDATED" => () => f.Employee.PutAsJsonAsync(route, new { requestId = f.RetryId, expectedVersion = 1, deviceCode = "TB-B", incidentDescription = "Rollback" }),
            _ => () => f.Employee.PutAsJsonAsync(route, new { requestId = f.RetryId, expectedVersion = 1, addImages = new[] { Image() } })
        };
        await Expect(await operation(), HttpStatusCode.ServiceUnavailable);
        await f.Db(async db =>
        {
            var report = await db.DamageReports.SingleAsync(); Assert.Equal(1, report.RowVersion);
            Assert.Equal("TB-A", (await db.Devices.SingleAsync(x => x.Id == report.DeviceId)).DeviceCode);
            Assert.Single(await db.Set<DamageReportPendingDevice>().ToListAsync()); Assert.Empty(await db.Set<DamageReportReception>().ToListAsync());
            Assert.Single(await db.Set<DamageReportHistory>().ToListAsync()); Assert.Empty(await db.Set<DamageReportEvidence>().ToListAsync());
        });
        if (action == "EVIDENCE_ADDED") Assert.Equal(0, f.StoredFiles);
        await Expect(await operation(), HttpStatusCode.OK);
        if (action == "EVIDENCE_ADDED") Assert.Equal(1, f.StoredFiles);
    }

    [PostgreSqlFact]
    public async Task Validation_denies_spoofed_identity_invalid_content_time_images_and_scope()
    {
        await using var f = await Fixture.CreateAsync();
        await Expect(await f.Anonymous.PostAsJsonAsync(Url, f.CreateBody("TB-A")), HttpStatusCode.Unauthorized);
        foreach (var body in new object[]
        {
            new { requestId = Guid.Empty, deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "Description" },
            new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "OTHER", incidentDescription = "Description" },
            new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "   " },
            new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "Description", detectedAt = "2099-01-01T00:00:00Z" },
            new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "Description", reporterAccountId = Guid.NewGuid() },
            new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "Description", images = new[] { new { filename = "a.png", dataBase64 = Convert.ToBase64String(new byte[100]) } } },
            new { requestId = Guid.NewGuid(), deviceCode = "TB-A", incidentType = "DAMAGE", incidentDescription = "Description", images = new[] { Image("../a.png") } }
        }) await Expect(await f.Employee.PostAsJsonAsync(Url, body), HttpStatusCode.BadRequest);
        await Expect(await f.Outsider.PostAsJsonAsync(Url, f.CreateBody("TB-A")), HttpStatusCode.NotFound);
        await Expect(await f.Employee.GetAsync(Url + "?pageSize=101"), HttpStatusCode.BadRequest);
        await f.Db(async db => Assert.Empty(await db.DamageReports.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Repair_links_fix_the_device_and_database_rejects_mismatched_links_and_corrupt_state()
    {
        await using var f = await Fixture.CreateAsync();
        var r = await f.Create(f.Employee, f.CreateBody("TB-A"));
        await f.Db(async db =>
        {
            var report = await db.DamageReports.SingleAsync();
            var order = new ServiceOrder { DisplayCode = "SC-TEST", CreateRequestId = Guid.NewGuid(), CreateRequestHash = new string('A',64),
                CreatedByAccountId = f.ManagerId, CreatedAt = f.Host.Clock.GetUtcNow(), OrderType = ServiceOrderType.Repair, Status = ServiceWorkflowStatus.PendingApproval };
            var item = new ServiceOrderItem { ServiceOrderId = order.Id, DeviceId = report.DeviceId, ItemNo = 1,
                DeviceCodeSnapshot = "TB-A", DeviceNameSnapshot = "A", ExecutionMethod = ExecutionMethod.OnSite, Version = 1 };
            item.RepairDetail = new() { ServiceOrderItemId = item.Id, DamageReportId = report.Id };
            order.Items.Add(item); db.Add(order); await db.SaveChangesAsync();
        });
        await Expect(await f.Employee.PutAsJsonAsync(Route(r), new { requestId = Guid.NewGuid(), expectedVersion = 1, deviceCode = "TB-B" }), HttpStatusCode.Conflict);
        await f.Db(async db =>
        {
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE service_order_items SET device_id = (SELECT id FROM devices WHERE device_code = 'TB-B')"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM damage_report_history"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE damage_reports SET reporting_department_id = (SELECT id FROM departments WHERE code='PB_TEST'), row_version = row_version+1"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM damage_report_pending_devices"));
            Assert.Single(await db.Set<DamageReportPendingDevice>().ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task List_filters_and_stable_pagination_do_not_expose_other_departments()
    {
        await using var f = await Fixture.CreateAsync();
        var a = await f.Create(f.Employee, f.CreateBody("TB-A", incident: "ABNORMAL"));
        await f.Create(f.Employee, f.CreateBody("TB-B", incident: "MISSING"));
        await Expect(await f.Staff.PostAsJsonAsync(Route(a) + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = 1 }), HttpStatusCode.OK);
        await f.Create(f.Manager, f.CreateBody("TB-A"));
        Assert.Equal(3, (await Get(f.Staff, Url)).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await Get(f.Employee, Url)).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await Get(f.Employee, Url + "?receptionStatus=RECEIVED&incidentType=ABNORMAL&deviceCode=TB-A")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await Get(f.Employee, Url + "?search=%25")).GetProperty("totalCount").GetInt32());
        var p1 = await Get(f.Employee, Url + "?pageSize=1&sortBy=reportCode&descending=false");
        var p2 = await Get(f.Employee, Url + "?pageSize=1&page=2&sortBy=reportCode&descending=false");
        Assert.NotEqual(p1.GetProperty("items")[0].GetProperty("reportCode").GetString(), p2.GetProperty("items")[0].GetProperty("reportCode").GetString());
        Assert.Equal(2, p1.GetProperty("totalCount").GetInt32()); Assert.Equal(2, p2.GetProperty("totalCount").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Concurrent_receptions_keep_the_first_receiver_and_only_one_history_event()
    {
        await using var f = await Fixture.CreateAsync(); var r = await f.Create(f.Employee, f.CreateBody("TB-A"));
        var results = await Task.WhenAll(f.Manager.PostAsJsonAsync(Route(r) + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = 1 }),
            f.Staff.PostAsJsonAsync(Route(r) + "/receive", new { requestId = Guid.NewGuid(), expectedVersion = 1 }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var winner = await Json(results.Single(x => x.StatusCode == HttpStatusCode.OK), HttpStatusCode.OK);
        var actual = await Get(f.Staff, Route(r));
        Assert.Equal(winner.GetProperty("receivedByAccountCode").GetString(), actual.GetProperty("receivedByAccountCode").GetString());
        await f.Db(async db => { Assert.Single(await db.Set<DamageReportReception>().ToListAsync()); Assert.Equal(2, await db.Set<DamageReportHistory>().CountAsync()); Assert.Empty(await db.Set<DamageReportPendingDevice>().ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task New_migration_rolls_back_and_reapplies_without_modifying_existing_module_data()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Db(async db =>
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            await migrator.MigrateAsync("20261103064434_EquipmentDepartmentAndTransferConfirmation");
            Assert.Equal(3, await db.Devices.CountAsync()); Assert.Equal(5, await db.Accounts.CountAsync());
            await migrator.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(3, await db.Devices.CountAsync());
        });
        await f.Create(f.Employee, f.CreateBody("TB-A"));
    }

    private static string Route(JsonElement report) => Url + "/" + report.GetProperty("reportCode").GetString();
    private static async Task<JsonElement> Get(HttpClient client, string route) => await Json(await client.GetAsync(route), HttpStatusCode.OK);
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    { await Expect(response, expected); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone(); }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
        => Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private sealed class FailureInterceptor : SaveChangesInterceptor
    {
        public string? Action { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Action is { } action && data.Context!.ChangeTracker.Entries<DamageReportHistory>().Any(x => x.State == EntityState.Added && x.Entity.ActionCode == action))
            { Action = null; throw new DbUpdateException("Simulated report audit failure."); }
            return base.SavingChangesAsync(data, result, ct);
        }
    }
    private sealed class MemoryEvidence : IReportEvidenceStore
    {
        private readonly ConcurrentDictionary<string,byte[]> _objects = new();
        public int Count => _objects.Count;
        public Task<string> StoreAsync(ValidatedReportImage image, CancellationToken ct) { var key = Guid.NewGuid().ToString("N"); _objects[key] = image.Bytes; return Task.FromResult(key); }
        public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(_objects.TryGetValue(key, out var bytes) ? bytes : null);
        public Task DeleteUncommittedAsync(string key, CancellationToken ct) { _objects.TryRemove(key, out _); return Task.CompletedTask; }
    }
    private sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host, WebApplicationFactory<Program> api,
        FailureInterceptor failures, MemoryEvidence store) : IAsyncDisposable
    {
        public AccountApiTestHost Host => host;
        public FailureInterceptor Failures => failures;
        public int StoredFiles => store.Count;
        public HttpClient Manager { get; private set; } = null!;
        public HttpClient Staff { get; private set; } = null!;
        public HttpClient Employee { get; private set; } = null!;
        public HttpClient Peer { get; private set; } = null!;
        public HttpClient Outsider { get; private set; } = null!;
        public HttpClient Anonymous { get; private set; } = null!;
        public Guid EmployeeId { get; private set; }
        public Guid ManagerId { get; private set; }
        public Guid EquipmentDepartment { get; private set; }
        public Guid RetryId { get; } = Guid.NewGuid();
        public object CreateBody(string device, Guid? key = null, string incident = "DAMAGE") => new { requestId = key ?? Guid.NewGuid(), deviceCode = device, incidentType = incident, incidentDescription = " Description " };
        public Task<JsonElement> Create(HttpClient client, object body) => CreateCore(client, body);
        private static async Task<JsonElement> CreateCore(HttpClient client, object body) => await Json(await client.PostAsJsonAsync(Url, body), HttpStatusCode.Created);
        public async Task Db(Func<CsvcDbContext,Task> action) { await using var scope = api.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>()); }
        public static async Task<Fixture> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync(); var host = new AccountApiTestHost(database.ConnectionString);
            var failures = new FailureInterceptor(); var store = new MemoryEvidence();
            var api = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            { services.AddSingleton<IReportEvidenceStore>(store); services.AddDbContext<CsvcDbContext>(o => o.AddInterceptors(failures)); }));
            var f = new Fixture(database, host, api, failures, store);
            var seed = await host.SeedAsync(); f.EquipmentDepartment = seed.Department; f.ManagerId = seed.Manager;
            f.Manager = Client(api, seed.ManagerToken); f.Staff = Client(api, seed.StaffToken); f.Anonymous = api.CreateClient();
            await f.Db(async db =>
            {
                (await db.EquipmentSettings.SingleAsync()).EquipmentDepartmentId = seed.Department;
                var a = new Department { Code = "PB_A", Name = "Department A" }; var other = new Department { Code = "PB_OTHER", Name = "Other department" };
                var category = new DeviceCategory { DisplayCode = "L-SC", Name = "Incident devices" }; db.AddRange(a, other, category);
                foreach (var code in new[] { "TB-A", "TB-B", "TB-C" })
                {
                    var barcode = "BC" + code.Replace("-", "").PadRight(30, '0');
                    db.Add(new DeviceIdentity { DeviceCode = code, Barcode = barcode, ReservedAt = host.Clock.GetUtcNow() });
                    db.Devices.Add(new Device { DeviceCode = code, Barcode = barcode, Name = code, CategoryId = category.Id, DepartmentId = a.Id, Quantity = 1 });
                }
                foreach (var name in new[] { "EMPLOYEE", "PEER", "OUTSIDER" })
                {
                    var profile = new UserProfile { FullName = name, DepartmentId = name == "OUTSIDER" ? other.Id : a.Id };
                    var account = new Account { AccountCode = "TK_" + name, Email = name.ToLowerInvariant() + "@example.com", PasswordHash = "hash", RoleId = 3, UserProfileId = profile.Id };
                    var token = Guid.NewGuid().ToString("N"); db.AddRange(profile, account, AccountApiTestHost.Session(account.Id, token));
                    if (name == "EMPLOYEE") { f.Employee = Client(api, token); f.EmployeeId = account.Id; }
                    else if (name == "PEER") f.Peer = Client(api, token); else f.Outsider = Client(api, token);
                }
                await db.SaveChangesAsync();
            });
            return f;
        }
        private static HttpClient Client(WebApplicationFactory<Program> api, string token)
        { var client = api.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token); return client; }
        public async ValueTask DisposeAsync()
        { Manager.Dispose(); Staff.Dispose(); Employee.Dispose(); Peer.Dispose(); Outsider.Dispose(); Anonymous.Dispose(); await api.DisposeAsync(); await host.DisposeAsync(); await database.DisposeAsync(); }
    }
}
