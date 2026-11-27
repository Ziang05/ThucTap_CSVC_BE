using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Materials;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

public sealed class MaterialRecipientWorkflowTests
{
    private const string Root = "/api/v1/materials";
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using (response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent)
    { using (response) Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync()); }
    private static string Code(JsonElement doc) => doc.GetProperty("summary").GetProperty("code").GetString()!;
    private static async Task Seed(EquipmentStaffWorkflowTests.Fixture f, int quantity = 5)
    {
        await Json(await f.Staff.PostAsJsonAsync(Root + "/units", new { code = "CAI", name = "Piece", decimalPlaces = 0 }));
        await Json(await f.Staff.PostAsJsonAsync(Root, new { code = "VT1", name = "Part", unitCode = "CAI", kind = "Material" }));
        var receipt = await Json(await f.Staff.PostAsJsonAsync(Root + "/documents", new { requestId = Guid.NewGuid(), kind = "Receipt", reason = "Stock", reference = "Supplier", lines = new[] { new { materialCode = "VT1", quantity, condition = "New" } } }));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents/" + Code(receipt) + "/post", new { requestId = Guid.NewGuid(), version = 1 }));
    }
    private static object Change(Guid requestId, int quantity) => new { requestId, deviceCode = "TB-ROLE-001", kind = "Repair", target = "Power supply", beforeValue = "Faulty", afterValue = "Repaired", reason = "Repair", issue = new {
        reason = "Parts for repair", targetDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_DEPARTMENT",
        lines = new[] { new { materialCode = "VT1", quantity, condition = "New" } } } };
    private static object Return(Guid line, int quantity, Guid? requestId = null) => new { requestId = requestId ?? Guid.NewGuid(), kind = "ReturnUnused", reason = "Unused parts", lines = new[] { new { materialCode = "VT1", quantity, originalIssueLineId = line, condition = "New" } } };

    [PostgreSqlFact]
    public async Task Intervention_posts_new_issue_once_then_only_recipient_records_use_and_submits_return_for_QTTB_approval()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await Seed(f);
        var body = Change(Guid.NewGuid(), 3);
        var change = await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", body));
        var replay = await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", body));
        Assert.Equal(change.GetProperty("code").GetString(), replay.GetProperty("code").GetString());
        var issueCode = change.GetProperty("issueDocumentCode").GetString()!;
        var issue = await f.Department.GetFromJsonAsync<JsonElement>(Root + "/documents/" + issueCode);
        Assert.Equal("Completed", issue.GetProperty("summary").GetProperty("status").GetString());
        Assert.True(issue.GetProperty("canRecordConsumption").GetBoolean());
        Assert.Equal(0, issue.GetProperty("lines")[0].GetProperty("usedQuantity").GetDecimal());
        var line = issue.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var consumption = new { requestId = Guid.NewGuid(), issueLineId = line, quantity = 1, reason = "Installed" };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/uses", consumption), HttpStatusCode.Forbidden);
        await Expect(await f.Receiver.PostAsJsonAsync(Root + "/uses", consumption), HttpStatusCode.Forbidden);
        await Json(await f.Department.PostAsJsonAsync(Root + "/uses", consumption));
        await Json(await f.Department.PostAsJsonAsync(Root + "/uses", consumption));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/documents", Return(line, 2)), HttpStatusCode.Forbidden);
        var returnBody = Return(line, 2);
        var returned = await Json(await f.Department.PostAsJsonAsync(Root + "/documents", returnBody));
        Assert.Equal(Code(returned), Code(await Json(await f.Department.PostAsJsonAsync(Root + "/documents", returnBody))));
        Assert.Equal("PendingApproval", returned.GetProperty("summary").GetProperty("status").GetString());
        await f.InDb(async db => Assert.Equal(2, (await db.Set<MaterialBalance>().SingleAsync()).Quantity));
        await Expect(await f.Department.PostAsJsonAsync(Root + "/uses", new { requestId = Guid.NewGuid(), issueLineId = line, quantity = 1, reason = "Cannot use pending returns" }), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/documents", Return(line, 1)), HttpStatusCode.Conflict);
        var returnRoute = Root + "/documents/" + Code(returned);
        await Expect(await f.Staff.PostAsJsonAsync(returnRoute + "/post", new { requestId = Guid.NewGuid(), version = 1 }), HttpStatusCode.BadRequest);
        await Expect(await f.Department.PostAsJsonAsync(returnRoute + "/approve", new { requestId = Guid.NewGuid(), version = 1 }), HttpStatusCode.Forbidden);
        var approval = new { requestId = Guid.NewGuid(), version = 1 };
        await Expect(await f.Staff.PostAsJsonAsync(returnRoute + "/approve", approval));
        await Expect(await f.Staff.PostAsJsonAsync(returnRoute + "/approve", approval));
        var completed = await f.Department.GetFromJsonAsync<JsonElement>(returnRoute);
        Assert.Equal("Completed", completed.GetProperty("summary").GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, completed.GetProperty("summary").GetProperty("postedAt").ValueKind);
        await f.InDb(async db => {
            Assert.Equal(4, (await db.Set<MaterialBalance>().SingleAsync()).Quantity);
            Assert.Equal(1, await db.Set<EquipmentChange>().CountAsync());
            Assert.Equal(1, await db.Set<MaterialWorkUse>().CountAsync());
            Assert.Equal(0, await db.Set<EquipmentMaterialUse>().CountAsync());
            Assert.Equal(3, await db.Set<MaterialLedgerEntry>().CountAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [PostgreSqlFact]
    public async Task Inline_issue_and_intervention_roll_back_together_for_insufficient_stock_or_evidence_failure()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await Seed(f, 2);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/changes", Change(Guid.NewGuid(), 3)), HttpStatusCode.Conflict);
        await f.InDb(async db => {
            Assert.Equal(1, await db.Set<MaterialDocument>().CountAsync());
            Assert.Equal(2, (await db.Set<MaterialBalance>().SingleAsync()).Quantity);
            Assert.Empty(await db.Set<EquipmentChange>().ToArrayAsync());
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_inline_intervention() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Test intervention failure'; END $$;
                CREATE TRIGGER reject_inline_intervention BEFORE INSERT ON equipment_changes FOR EACH ROW EXECUTE FUNCTION reject_inline_intervention();
                """);
        });
        var body = Change(Guid.NewGuid(), 1);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/changes", body), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => {
            Assert.Equal(1, await db.Set<MaterialDocument>().CountAsync());
            Assert.Equal(1, await db.Set<MaterialLedgerEntry>().CountAsync());
            Assert.Equal(2, (await db.Set<MaterialBalance>().SingleAsync()).Quantity);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_inline_intervention ON equipment_changes; DROP FUNCTION reject_inline_intervention()");
        });
        await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", body));
    }

    [PostgreSqlFact]
    public async Task Concurrent_return_requests_cannot_reserve_the_same_material_twice_and_rejection_releases_it()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await Seed(f);
        var change = await Json(await f.Staff.PostAsJsonAsync(Root + "/changes", Change(Guid.NewGuid(), 3)));
        var issue = await f.Department.GetFromJsonAsync<JsonElement>(Root + "/documents/" + change.GetProperty("issueDocumentCode").GetString());
        var line = issue.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var replies = await Task.WhenAll(f.Department.PostAsJsonAsync(Root + "/documents", Return(line, 2)), f.Department.PostAsJsonAsync(Root + "/documents", Return(line, 2)));
        Assert.Single(replies, r => r.IsSuccessStatusCode); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        var returned = await Json(replies.Single(r => r.IsSuccessStatusCode));
        foreach (var reply in replies.Where(r => !r.IsSuccessStatusCode)) reply.Dispose();
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/documents/" + Code(returned) + "/reject", new { requestId = Guid.NewGuid(), version = 1, reason = "Incorrect quantity" }));
        await Json(await f.Department.PostAsJsonAsync(Root + "/uses", new { requestId = Guid.NewGuid(), issueLineId = line, quantity = 3, reason = "All installed" }));
        await Expect(await f.Department.PostAsJsonAsync(Root + "/documents", Return(line, 1)), HttpStatusCode.Conflict);
        await f.InDb(async db => Assert.Equal(2, (await db.Set<MaterialBalance>().SingleAsync()).Quantity));
    }
}
