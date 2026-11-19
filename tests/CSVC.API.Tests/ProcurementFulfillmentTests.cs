using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.ProcurementRequests;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class ProcurementFulfillmentTests
{
    private const string Root = "/api/v1/procurement-requests";
    private static object Line(string code, string kind = "Service") => new { sourceLineCode = code, kind, description = "Complete requested work", quantity = 1, unit = "Lan", categoryCode = kind == "Equipment" ? "CAT_ROLE" : null };
    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    { using (response) { Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }
    private static async Task<string> Create(EquipmentStaffWorkflowTests.Fixture f, string external, object[]? lines = null, string department = "PB_TARGET")
        => (await Read(await f.Staff.PostAsJsonAsync(Root + "/simulated-events", new { eventId = Guid.NewGuid(), externalCode = external, sourceVersion = 1, action = "Create", request = new { requestType = "Purchase", title = external, requestingDepartmentCode = department, sourceStatus = "Submitted", createdAt = "2026-10-21T08:00:00Z", updatedAt = "2026-10-21T08:00:00Z", lines = lines ?? [Line("S1"), Line("S2")] } }))).GetProperty("displayCode").GetString()!;
    private static Task<JsonElement> Detail(EquipmentStaffWorkflowTests.Fixture f, string code) => f.Staff.GetFromJsonAsync<JsonElement>(Root + "/" + code);
    private static int Version(JsonElement d) => d.GetProperty("summary").GetProperty("version").GetInt32();
    private static object Evidence(int version, Guid? id = null, string outcome = "Completed", params string[] codes) => new { requestId = id ?? Guid.NewGuid(), version, lines = (codes.Length == 0 ? ["S1", "S2"] : codes).Select(sourceLineCode => new { sourceLineCode, outcome, provider = "Qualified contractor", evidenceReference = "BB-DV-001", description = "Full result and supporting document" }).ToArray() };
    private static async Task Accept(EquipmentStaffWorkflowTests.Fixture f, string code, bool accepted = true)
        => await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/accept-results", new { requestId = Guid.NewGuid(), version = Version(await Detail(f, code)), accepted, reason = accepted ? null : "Results need correction" }));

    [PostgreSqlTheory]
    [InlineData("Completed", "Success")]
    [InlineData("Failed", "CompletedWithIssues")]
    public async Task All_service_lines_require_evidence_receiver_acceptance_and_idempotent_commands(string outcome, string result)
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var code = await Create(f, "SERVICE-FULL"); var route = Root + "/" + code;
        var d = await Detail(f, code);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version = Version(d) }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(d), codes: ["S1"])), HttpStatusCode.BadRequest);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(d))), HttpStatusCode.Forbidden);
        var command = Evidence(Version(d), outcome: outcome);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", command));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", command));
        d = await Detail(f, code); Assert.Equal(2, d.GetProperty("serviceEvidence").GetProperty("lines").GetArrayLength());
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version = Version(d), failureReason = "Cannot bypass acceptance" }), HttpStatusCode.Conflict);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/accept-results", new { requestId = Guid.NewGuid(), version = Version(d), accepted = true }), HttpStatusCode.Forbidden);
        await Expect(await f.Receiver.GetAsync(route), HttpStatusCode.NotFound);
        await Expect(await f.Receiver.GetAsync(route + "/history"), HttpStatusCode.NotFound);
        await Expect(await f.Receiver.PostAsJsonAsync(route + "/accept-results", new { requestId = Guid.NewGuid(), version = Version(d), accepted = true }), HttpStatusCode.NotFound);
        var acceptance = new { requestId = Guid.NewGuid(), version = Version(d), accepted = true };
        await Expect(await f.Department.PostAsJsonAsync(route + "/accept-results", acceptance));
        await Expect(await f.Department.PostAsJsonAsync(route + "/accept-results", acceptance));
        d = await Detail(f, code);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(d))), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version = Version(d) }));
        await Expect(await f.Department.PostAsJsonAsync(route + "/accept-results", acceptance)); // Network retry after closing.
        Assert.Equal(result, (await Detail(f, code)).GetProperty("result").GetString());
        await f.InDb(async db => { Assert.Single(await db.Set<ProcurementRequestEvent>().Where(e => e.Action == "SERVICE_EVIDENCE_RECORDED").ToListAsync()); Assert.Single(await db.Set<ProcurementRequestEvent>().Where(e => e.Action == "RESULT_ACCEPTANCE_RECORDED").ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Evidence_audit_failure_rolls_back_results_version_and_command_identity_before_retry()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var code = await Create(f, "SERVICE-ROLLBACK"); var route = Root + "/" + code;
        var before = await Detail(f, code); var command = Evidence(Version(before));
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_service_evidence_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action='SERVICE_EVIDENCE_RECORDED' THEN RAISE EXCEPTION 'Test audit rollback'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_service_evidence_audit BEFORE INSERT ON procurement_request_events FOR EACH ROW EXECUTE FUNCTION fail_service_evidence_audit();
            """));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", command), HttpStatusCode.ServiceUnavailable);
        var after = await Detail(f, code); Assert.Equal(Version(before), Version(after)); Assert.Equal(JsonValueKind.Null, after.GetProperty("serviceEvidence").ValueKind);
        await f.InDb(async db => { Assert.Empty(await db.Set<ProcurementRequestEvent>().Where(e => e.CommandId != null).ToListAsync()); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_service_evidence_audit ON procurement_request_events; DROP FUNCTION fail_service_evidence_audit()"); });
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", command));
    }

    [PostgreSqlFact]
    public async Task Rejected_service_evidence_can_be_corrected_without_overwriting_history()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var code = await Create(f, "SERVICE-CORRECT"); var route = Root + "/" + code;
        var id = Guid.NewGuid();
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(await Detail(f, code)), id, "Failed")));
        await Accept(f, code, false);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version = Version(await Detail(f, code)) }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(await Detail(f, code)), id)), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(await Detail(f, code)))));
        Assert.Equal(JsonValueKind.Null, (await Detail(f, code)).GetProperty("acceptance").ValueKind);
        await Accept(f, code);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version = Version(await Detail(f, code)) }));
        var history = await f.Department.GetFromJsonAsync<JsonElement>(route + "/history");
        Assert.Equal(2, history.GetProperty("items").EnumerateArray().Count(e => e.GetProperty("action").GetString() == "SERVICE_EVIDENCE_RECORDED"));
        Assert.Contains("Failed", history.ToString());
    }

    [PostgreSqlFact]
    public async Task Only_manager_can_close_rejected_results_as_failed_and_departments_see_only_their_own_requests()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var code = await Create(f, "SERVICE-REJECT"); var route = Root + "/" + code;
        await Create(f, "OTHER-DEPARTMENT", department: "PB_TEST");
        var page = await f.Department.GetFromJsonAsync<JsonElement>(Root);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(await Detail(f, code)), outcome: "Failed")));
        await Accept(f, code, false);
        var close = new { version = Version(await Detail(f, code)), failureReason = "Unit declined; manager records final unsuccessful outcome" };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", close), HttpStatusCode.Conflict);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/complete", close));
        Assert.Equal("Failed", (await Detail(f, code)).GetProperty("result").GetString());
    }

    [PostgreSqlFact]
    public async Task Mixed_equipment_and_services_cannot_be_accepted_until_actual_whole_handover()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var code = await Create(f, "SERVICE-MIXED", [Line("E1", "Equipment"), Line("S1")]); var route = Root + "/" + code;
        await Expect(await f.Staff.PostAsJsonAsync(route + "/service-evidence", Evidence(Version(await Detail(f, code)), codes: ["S1"])));
        await Expect(await f.Department.PostAsJsonAsync(route + "/accept-results", new { requestId = Guid.NewGuid(), version = Version(await Detail(f, code)), accepted = true }), HttpStatusCode.Conflict);
        var transfer = await Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new { sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Full equipment allocation", items = new[] { new { sourceLineCode = "E1", deviceCode = "TB-ROLE-001" } } }), HttpStatusCode.Created);
        var transferRoute = "/api/v1/device-transfers/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PostAsync(transferRoute + "/confirm-receipt-intent", null));
        await Expect(await f.Department.PostAsJsonAsync(transferRoute + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }));
        await Accept(f, code);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/complete", new { version = Version(await Detail(f, code)) }));
        Assert.Equal("Success", (await Detail(f, code)).GetProperty("result").GetString());
    }

    [PostgreSqlFact]
    public async Task Specialized_on_site_repair_requires_reviewable_reason_and_preserves_command_fingerprint()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        var source = await Read(await f.Staff.PostAsJsonAsync(Root + "/simulated-events", new { eventId = Guid.NewGuid(), externalCode = "SPECIALIZED-REPAIR", sourceVersion = 1, action = "Create", request = new { requestType = "Repair", title = "Specialized equipment", requestingDepartmentCode = "PB_TARGET", sourceStatus = "Submitted", createdAt = "2026-10-21T08:00:00Z", updatedAt = "2026-10-21T08:00:00Z", lines = new[] { new { sourceLineCode = "R1", kind = "Equipment", description = "Calibrate equipment", quantity = 1, unit = "Cai", deviceCode = "TB-ROLE-001", specialization = "Specialized" } } } }));
        var code = source.GetProperty("displayCode").GetString(); var id = Guid.NewGuid();
        object Body(string? reason) => new { requestId = id, sourceRequestCode = code, orderType = "Repair", specializedHandlingReason = reason, items = new[] { new { sourceLineCode = "R1", deviceCode = "TB-ROLE-001", executionMethod = "OnSite", workDescription = "Qualified technician attends on site" } } };
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", Body(null)), HttpStatusCode.BadRequest);
        var order = await Read(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", Body("Certified on-site technician; no transport permitted")), HttpStatusCode.Created);
        Assert.Equal("PendingApproval", order.GetProperty("status").GetString());
        Assert.Contains("Certified", order.GetProperty("specializedHandlingReason").GetString());
        var replay = await Read(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", Body("Certified on-site technician; no transport permitted")), HttpStatusCode.Created);
        Assert.Equal(order.GetProperty("displayCode").GetString(), replay.GetProperty("displayCode").GetString());
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", Body("Changed exception")), HttpStatusCode.Conflict);
    }
}
