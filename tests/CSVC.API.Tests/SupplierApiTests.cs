using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using CSVC.Core.Common;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Suppliers.DTOs;
using CSVC.UseCases.Suppliers.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSVC.API.Tests;

public sealed partial class SupplierApiTests
{
    private const string Root = "/api/v1/suppliers";
    private const string History = "/api/v1/history/suppliers";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Mapping_normalizes_fields_and_preserves_identity_display_code_and_status()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<SupplierMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        mapper.ConfigurationProvider.AssertConfigurationIsValid();
        var supplier = new Supplier { DisplayCode = "NCC_FIXED", Name = "Before", Status = EntityStatus.Locked };
        var id = supplier.Id;
        mapper.Map(new UpdateSupplierRequest { Name = "  Máy\t  tính  ".Normalize(NormalizationForm.FormD),
            Description = "  Mô tả  ", Notes = "  Ghi chú  " }, supplier);
        Assert.Equal(id, supplier.Id);
        Assert.Equal("NCC_FIXED", supplier.DisplayCode);
        Assert.Equal(EntityStatus.Locked, supplier.Status);
        Assert.Equal("Máy tính", supplier.Name);
        Assert.Equal("Mô tả", supplier.Description);
        Assert.Equal("Ghi chú", supplier.Notes);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateSupplierRequest>("{\"name\":\"New\",\"displayCode\":\"FORGED\"}", Json));
    }

    [PostgreSqlFact]
    public async Task Create_generates_public_code_and_keeps_description_notes_and_UUID_separate()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        var response = await client.PostAsJsonAsync(Root, new { name = "  Máy\t tính ".Normalize(NormalizationForm.FormD),
            description = " Thiết bị văn phòng ", notes = " Nội bộ " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await Read<SupplierDetailDto>(response);
        Assert.StartsWith("NCC", detail.DisplayCode);
        Assert.Equal("Máy tính", detail.Name);
        Assert.Equal("Thiết bị văn phòng", detail.Description);
        Assert.Equal("Nội bộ", detail.Notes);
        Assert.Equal(EntityStatus.Active, detail.Status);
        Assert.Equal("TK_MANAGER", detail.Management!.CreatedByAccountCode);
        Assert.Equal("Test Manager", detail.Management.CreatedByFullName);
        Assert.Equal(factory.Clock.GetUtcNow(), detail.Management.CreatedAt);
        Assert.Equal(64, detail.Version!.Length);
        Assert.Null(detail.ReceiptCount);
        Assert.EndsWith(detail.DisplayCode, response.Headers.Location!.ToString());
        var body = await Read<JsonElement>(response);
        Assert.False(body.TryGetProperty("id", out _));
        Assert.False(body.GetProperty("management").TryGetProperty("createdBy", out _));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var entity = await db.Suppliers.SingleAsync();
        Assert.DoesNotContain(entity.Id.ToString(), await response.Content.ReadAsStringAsync());
        var audit = await db.SupplierHistories.SingleAsync();
        Assert.Equal("SUPPLIER_CREATED", audit.ActionCode);
        Assert.Equal(AuditOutcome.Success, audit.Outcome);
        Assert.Equal(entity.Id, audit.SupplierId);
        Assert.Equal(seed.Manager, audit.ActorAccountId);
        Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), audit.RequestId.ToString());
        Assert.Null(audit.BeforeData);
        Assert.DoesNotContain(entity.Id.ToString(), audit.AfterData!);
        Assert.Equal("Nội bộ", JsonDocument.Parse(audit.AfterData!).RootElement.GetProperty("notes").GetString());
    }

    [PostgreSqlFact]
    public async Task Both_equipment_roles_write_but_department_staff_only_read_basic_data()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Root)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Root, new { name = "Unauthorized" })).StatusCode);
        foreach (var client in new[] { manager, staff })
        {
            var target = await Create(client, client == manager ? "Manager" : "Staff", notes: "Internal");
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = target.Name, notes = "Changed" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Root}/{target.DisplayCode}/lock", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Root}/{target.DisplayCode}/lock")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Root}/{target.DisplayCode}/history")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Root}/{target.DisplayCode}/lock-impact")).StatusCode);
        }
        var item = await Create(manager, "Department view", notes: "SECRET");
        await ChangeRole(factory, seed.Staff, 3);
        var body = await staff.GetFromJsonAsync<JsonElement>($"{Root}/{item.DisplayCode}");
        foreach (var field in new[] { "id", "notes", "management", "version", "devices" }) Assert.False(body.TryGetProperty(field, out _));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Root)).StatusCode);
        foreach (var response in new[] {
            await staff.PostAsJsonAsync(Root, new { name = "Denied" }),
            await staff.PostAsJsonAsync(Root, new { name = " " }),
            await staff.PutAsJsonAsync($"{Root}/{item.DisplayCode}", new { name = "Denied" }),
            await staff.PutAsync($"{Root}/{item.DisplayCode}/lock", null),
            await staff.DeleteAsync($"{Root}/{item.DisplayCode}/lock"),
            await staff.GetAsync($"{Root}/{item.DisplayCode}/history"), await staff.GetAsync(History),
            await staff.GetAsync($"{Root}/{item.DisplayCode}/lock-impact") })
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(3, await db.Suppliers.CountAsync());
        Assert.Equal(5, await db.SupplierHistories.CountAsync(h => h.Outcome == AuditOutcome.Denied));
        Assert.False(await db.SupplierHistories.AnyAsync(h => h.ActionCode == "DENIED"));
    }

    [PostgreSqlFact]
    public async Task Manual_codes_support_64_characters_encoded_slashes_and_reserved_words()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        foreach (var code in new[] { "ncc/2026.01", new string('X', 64), "history" })
        {
            var response = await client.PostAsJsonAsync(Root, new { name = code, displayCode = " " + code + " " });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var target = await Read<SupplierDetailDto>(response);
            Assert.Equal(code.ToUpperInvariant(), target.DisplayCode);
            var route = $"{Root}/{Uri.EscapeDataString(target.DisplayCode)}";
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(route, new { name = code + " updated" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync(route + "/lock", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(route + "/lock")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, new { name = "Too long", displayCode = new string('X', 65) })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Validation_and_normalized_duplicates_preserve_existing_records_and_audit_attempted_action()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        var target = await Create(client, "Máy tính", "NCC_PC");
        foreach (var body in new object[] { new { name = " " }, new { name = new string('X', 201) },
            new { name = "Valid", description = new string('X', 4001) }, new { name = "Valid", notes = new string('X', 4001) },
            new { name = "Valid", status = "Locked" }, new { name = "Valid", id = Guid.NewGuid() },
            new { name = "Valid", displayCode = "INVALID CODE" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, body)).StatusCode);
        foreach (var body in new object[] { new { name = "New", displayCode = "CHANGED" }, new { name = "New", version = "bad" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", body)).StatusCode);
        await Code(await client.PostAsJsonAsync(Root, new { name = "Other", displayCode = "ncc_pc" }), HttpStatusCode.Conflict, "SUPPLIER_CODE_CONFLICT");
        await client.PutAsync($"{Root}/{target.DisplayCode}/lock", null);
        await Code(await client.PostAsJsonAsync(Root, new { name = " MÁY\t TÍNH ".Normalize(NormalizationForm.FormD) }), HttpStatusCode.Conflict, "SUPPLIER_NAME_CONFLICT");
        await Code(await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = "Changed" }), HttpStatusCode.Conflict, "SUPPLIER_LOCKED");
        await Code(await client.DeleteAsync($"{Root}/{target.DisplayCode}"), HttpStatusCode.Forbidden, "SUPPLIER_DELETE_FORBIDDEN");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Root}/MISSING")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{Root}/MISSING", new { name = "Missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Root}/INVALID%20CODE")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal("Máy tính", (await db.Suppliers.SingleAsync()).Name);
        Assert.Equal(7, await db.SupplierHistories.CountAsync(h => h.ActionCode == "SUPPLIER_CREATED" && h.ReasonCode == "VALIDATION_FAILED"));
        Assert.Equal(2, await db.SupplierHistories.CountAsync(h => h.ActionCode == "SUPPLIER_UPDATED" && h.ReasonCode == "VALIDATION_FAILED"));
        Assert.False(await db.SupplierHistories.AnyAsync(h => h.ActionCode == "FAILED" || h.ActionCode == "DENIED"));
        Assert.Single(await db.SupplierHistories.Where(h => h.ActionCode == "DELETE_DENIED" && h.Outcome == AuditOutcome.Denied).ToListAsync());
        Assert.All(await db.SupplierHistories.Where(h => h.ReasonCode == "VALIDATION_FAILED").ToListAsync(), h => Assert.Null(h.AfterData));
    }

    [PostgreSqlFact]
    public async Task Update_tracks_notes_immutable_code_version_and_no_change_without_fake_success()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.StaffToken);
        var target = await Create(client, "Máy chiếu", "NCC_PROJECTOR", "Before");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}",
            new { name = target.Name, notes = "After", version = target.Version })).StatusCode);
        await Code(await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = "Stale", version = target.Version }),
            HttpStatusCode.Conflict, "SUPPLIER_VERSION_CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}",
            new { name = target.Name, notes = " After " })).StatusCode);
        var detail = (await client.GetFromJsonAsync<SupplierDetailDto>($"{Root}/{target.DisplayCode}", Json))!;
        Assert.Equal(target.DisplayCode, detail.DisplayCode);
        Assert.Equal("After", detail.Notes);
        Assert.NotEqual(target.Version, detail.Version);
        Assert.Equal(target.Management!.CreatedAt, detail.Management!.CreatedAt);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var audit = await db.SupplierHistories.Where(h => h.ActionCode == "SUPPLIER_UPDATED").ToListAsync();
        var success = Assert.Single(audit, h => h.Outcome == AuditOutcome.Success);
        Assert.Equal(new[] { "notes" }, JsonSerializer.Deserialize<string[]>(success.ChangedFields!));
        Assert.Single(audit, h => h.Outcome == AuditOutcome.NoChange && h.ChangedFields == null);
        Assert.Single(audit, h => h.Outcome == AuditOutcome.Failed && h.ReasonCode == "SUPPLIER_VERSION_CONFLICT");
    }

    [PostgreSqlFact]
    public async Task List_filters_sorts_pages_and_lock_impact_reports_unknown_device_counts()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        await Create(client, "Alpha", "NCC_ALPHA");
        await Create(client, "Beta", "NCC_BETA");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{Root}/NCC_BETA/lock", null)).StatusCode);
        foreach (var sort in new[] { "Name", "DisplayCode", "Status" })
        foreach (var direction in new[] { "Asc", "Desc" })
        {
            var list = await client.GetFromJsonAsync<JsonElement>($"{Root}?sortBy={sort}&sortDirection={direction}");
            var values = list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty(char.ToLowerInvariant(sort[0]) + sort[1..]).GetString()).ToArray();
            var expected = direction == "Asc" ? values.OrderBy(x => x, StringComparer.Ordinal) : values.OrderByDescending(x => x, StringComparer.Ordinal);
            Assert.Equal(expected, values);
        }
        var page = await client.GetFromJsonAsync<JsonElement>($"{Root}?search=BeTa&status=Locked&pageSize=1");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("NCC_BETA", page.GetProperty("items")[0].GetProperty("displayCode").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("items")[0].GetProperty("receiptCount").ValueKind);
        Assert.False(page.GetProperty("items")[0].TryGetProperty("notes", out _));
        Assert.False(page.GetProperty("items")[0].TryGetProperty("id", out _));
        var empty = await client.GetFromJsonAsync<JsonElement>($"{Root}?search=missing");
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var impact = await client.GetFromJsonAsync<JsonElement>($"{Root}/NCC_BETA/lock-impact");
        Assert.Equal(JsonValueKind.Null, impact.GetProperty("receiptCount").ValueKind);
        Assert.False(impact.GetProperty("linkCountsComplete").GetBoolean());
        Assert.Equal("Locked", impact.GetProperty("status").GetString());
        foreach (var query in new[] { "page=0", "pageSize=101", "sortBy=999", "sortDirection=999", "status=999" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Root}?{query}")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<CsvcDbContext>().Suppliers.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Renaming_rejects_active_and_locked_duplicates_and_unlock_is_idempotent()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.StaffToken);
        var first = await Create(client, "Máy in", "NCC_PRINTER");
        var second = await Create(client, "Máy scan", "NCC_SCANNER");
        await Code(await client.PutAsJsonAsync($"{Root}/{first.DisplayCode}", new { name = " MÁY\t SCAN " }), HttpStatusCode.Conflict, "SUPPLIER_NAME_CONFLICT");
        await client.PutAsync($"{Root}/{second.DisplayCode}/lock", null);
        await Code(await client.PutAsJsonAsync($"{Root}/{first.DisplayCode}", new { name = "Máy scan" }), HttpStatusCode.Conflict, "SUPPLIER_NAME_CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Root}/{second.DisplayCode}/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Root}/{second.DisplayCode}/lock")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal("Máy in", (await db.Suppliers.SingleAsync(c => c.DisplayCode == first.DisplayCode)).Name);
        var target = await db.Suppliers.SingleAsync(c => c.DisplayCode == second.DisplayCode);
        Assert.Equal(EntityStatus.Active, target.Status);
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.SupplierId == target.Id && h.ActionCode == "SUPPLIER_UNLOCKED" && h.Outcome == AuditOutcome.Success));
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.SupplierId == target.Id && h.ActionCode == "SUPPLIER_UNLOCKED" && h.Outcome == AuditOutcome.NoChange));
        Assert.Equal(target.Id, (await db.SupplierHistories.SingleAsync(h => h.SupplierId == target.Id && h.ActionCode == "SUPPLIER_CREATED")).SupplierId);
    }

    [PostgreSqlFact]
    public async Task Concurrent_names_codes_locks_and_stale_edits_have_serial_results()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        var names = await Task.WhenAll(client.PostAsJsonAsync(Root, new { name = " Máy\t tính " }), client.PostAsJsonAsync(Root, new { name = "MÁY TÍNH" }));
        Assert.Single(names, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(names, r => r.StatusCode == HttpStatusCode.Conflict);
        var codes = await Task.WhenAll(client.PostAsJsonAsync(Root, new { name = "One", displayCode = "NCC_DUP" }), client.PostAsJsonAsync(Root, new { name = "Two", displayCode = "ncc_dup" }));
        Assert.Single(codes, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(codes, r => r.StatusCode == HttpStatusCode.Conflict);
        var target = await Create(client, "Version race");
        var edits = await Task.WhenAll(client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = "Version A", version = target.Version }),
            client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = "Version B", version = target.Version }));
        Assert.Single(edits, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(edits, r => r.StatusCode == HttpStatusCode.Conflict);
        var locks = await Task.WhenAll(client.PutAsync($"{Root}/{target.DisplayCode}/lock", null), client.PutAsync($"{Root}/{target.DisplayCode}/lock", null));
        Assert.All(locks, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(3, await db.Suppliers.CountAsync());
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.ActionCode == "SUPPLIER_LOCKED" && h.Outcome == AuditOutcome.Success));
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.ActionCode == "SUPPLIER_LOCKED" && h.Outcome == AuditOutcome.NoChange));
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.ActionCode == "SUPPLIER_UPDATED" && h.Outcome == AuditOutcome.Success));
    }

    [PostgreSqlFact]
    public async Task Audit_failure_rolls_back_each_mutation_and_failed_audit_never_reports_success()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        var target = await Create(client, "Before");
        foreach (var action in new[] { "SUPPLIER_CREATED", "SUPPLIER_UPDATED", "SUPPLIER_LOCKED", "SUPPLIER_UNLOCKED" })
        {
            if (action == "SUPPLIER_UNLOCKED") await client.PutAsync($"{Root}/{target.DisplayCode}/lock", null);
            factory.Failures.FailNextSupplierAction = action;
            var response = action switch
            {
                "SUPPLIER_CREATED" => await client.PostAsJsonAsync(Root, new { name = "Rollback" }),
                "SUPPLIER_UPDATED" => await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = "Changed" }),
                "SUPPLIER_LOCKED" => await client.PutAsync($"{Root}/{target.DisplayCode}/lock", null),
                _ => await client.DeleteAsync($"{Root}/{target.DisplayCode}/lock")
            };
            await Code(response, HttpStatusCode.ServiceUnavailable, "SUPPLIER_UNAVAILABLE");
            var detail = (await client.GetFromJsonAsync<SupplierDetailDto>($"{Root}/{target.DisplayCode}", Json))!;
            Assert.Equal("Before", detail.Name);
            Assert.Equal(action == "SUPPLIER_UNLOCKED" ? EntityStatus.Locked : EntityStatus.Active, detail.Status);
        }
        factory.Failures.FailNextSupplierAction = "SUPPLIER_CREATED";
        await Code(await client.PostAsJsonAsync(Root, new { name = " " }), HttpStatusCode.ServiceUnavailable, "SUPPLIER_UNAVAILABLE");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(1, await db.Suppliers.CountAsync());
        Assert.Equal(2, await db.SupplierHistories.CountAsync(h => h.Outcome == AuditOutcome.Success));
        Assert.Equal(4, await db.SupplierHistories.CountAsync(h => h.ReasonCode == "SUPPLIER_UNAVAILABLE"));
    }

    [PostgreSqlFact]
    public async Task History_uses_event_snapshots_filters_and_redacts_raw_data_and_request_ids_by_role()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        var target = await Create(manager, "Old name", "NCC_HISTORY", "Internal");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await staff.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = "New name", description = "New description", notes = "Changed" });
        var global = await staff.GetFromJsonAsync<JsonElement>($"{History}?search=old%20name&actionCode=SUPPLIER_CREATED&outcome=Success&actorAccountCode=tk_manager");
        Assert.Equal(1, global.GetProperty("totalCount").GetInt32());
        var row = global.GetProperty("items")[0];
        Assert.Equal("Old name", row.GetProperty("name").GetString());
        Assert.Equal("NCC_HISTORY", row.GetProperty("displayCode").GetString());
        foreach (var field in new[] { "supplierId", "actorAccountId", "requestId", "beforeData", "afterData", "changedFields", "reasonCode" })
            Assert.False(row.TryGetProperty(field, out _));
        var id = row.GetProperty("id").GetGuid();
        var managerDetail = await manager.GetFromJsonAsync<JsonElement>($"{History}/{id}");
        var staffDetail = await staff.GetFromJsonAsync<JsonElement>($"{History}/{id}");
        Assert.True(managerDetail.TryGetProperty("requestId", out _));
        Assert.False(staffDetail.TryGetProperty("requestId", out _));
        Assert.Equal("Old name", staffDetail.GetProperty("afterData").GetProperty("name").GetString());
        Assert.False(staffDetail.GetProperty("afterData").TryGetProperty("id", out _));
        var updated = await staff.GetFromJsonAsync<JsonElement>($"{History}?displayCode=ncc_history&actorAccountCode=TK_STAFF&actionCode=SUPPLIER_UPDATED");
        var updateId = updated.GetProperty("items")[0].GetProperty("id").GetGuid();
        var updateDetail = await staff.GetFromJsonAsync<JsonElement>($"{Root}/{target.DisplayCode}/history/{updateId}");
        Assert.Equal(new[] { "name", "description", "notes" }, updateDetail.GetProperty("changedFields").EnumerateArray().Select(x => x.GetString()));
        var timed = await staff.GetFromJsonAsync<JsonElement>($"{History}?from=2026-10-21T19:00:30%2B07:00&to=2026-10-21T19:01:00%2B07:00");
        Assert.Equal(1, timed.GetProperty("totalCount").GetInt32());
        Assert.Equal("SUPPLIER_UPDATED", timed.GetProperty("items")[0].GetProperty("actionCode").GetString());
        var another = await Create(manager, "Another");
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{Root}/{another.DisplayCode}/history/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{History}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await manager.DeleteAsync($"{History}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await manager.PostAsJsonAsync(History, new { })).StatusCode);
    }

    [PostgreSqlFact]
    public async Task History_handles_null_actors_targets_ties_and_sanitizes_imported_JSON()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        var ids = new List<Guid>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            for (var i = 0; i < 3; i++)
            {
                var history = new SupplierHistory { RequestId = Guid.NewGuid(), ActionCode = "SUPPLIER_UPDATED", Outcome = AuditOutcome.Success,
                    OccurredAt = factory.Clock.GetUtcNow(), BeforeData = "{\"name\":\"Old\",\"notes\":null,\"id\":\"INTERNAL_UUID\",\"password\":\"SECRET\"}",
                    AfterData = "{\"display_code\":\"NCC_IMPORTED\",\"name\":\"Imported\",\"notes\":\"Safe\",\"token\":\"SECRET\",\"description\":{\"password\":\"SECRET\"}}",
                    ChangedFields = "[\"id\",\"token\",\"notes\"]", ReasonCode = "SECRET_REASON" };
                db.SupplierHistories.Add(history);
                ids.Add(history.Id);
            }
            await db.SaveChangesAsync();
        }
        var seen = new HashSet<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var response = await client.GetFromJsonAsync<JsonElement>($"{History}?pageSize=1&page={page}");
            Assert.Equal(3, response.GetProperty("totalCount").GetInt32());
            var row = response.GetProperty("items")[0];
            Assert.Equal(JsonValueKind.Null, row.GetProperty("actorAccountCode").ValueKind);
            Assert.True(seen.Add(row.GetProperty("id").GetGuid()));
        }
        foreach (var sort in new[] { "OccurredAt", "ActionCode", "Outcome", "ActorAccountCode" })
        foreach (var direction in new[] { "Asc", "Desc" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{History}?sortBy={sort}&sortDirection={direction}")).StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"{History}/{ids[0]}");
        var raw = detail.GetRawText();
        Assert.DoesNotContain("SECRET", raw);
        Assert.DoesNotContain("INTERNAL_UUID", raw);
        Assert.False(detail.GetProperty("afterData").TryGetProperty("description", out _));
        Assert.Equal(new[] { "display_code", "name", "notes" }, detail.GetProperty("changedFields").EnumerateArray().Select(x => x.GetString()));
        foreach (var query in new[] { "page=0", "pageSize=101", "outcome=999", "sortBy=999", "sortDirection=999", "from=2026-10-23&to=2026-10-21" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{History}?{query}")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Session_revocation_and_role_changes_take_effect_before_supplier_mutations()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.StaffToken);
        await Create(client, "Before revoke");
        await ChangeRole(factory, seed.Staff, 3);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root, new { name = "After demotion" })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            (await db.AuthSessions.SingleAsync(s => s.AccountId == seed.Staff)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root, new { name = "After revoke" })).StatusCode);
        await using var readScope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await readScope.ServiceProvider.GetRequiredService<CsvcDbContext>().Suppliers.CountAsync());
    }

    private static HttpClient Client(AccountApiTestHost factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static async Task<SupplierDetailDto> Create(HttpClient client, string name, string? displayCode = null, string? notes = null)
    {
        var response = await client.PostAsJsonAsync(Root, new { name, displayCode, notes });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Read<SupplierDetailDto>(response);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response) => JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), Json)!;
    private static async Task Code(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Read<JsonElement>(response)).GetProperty("code").GetString());
    }
    private static async Task ChangeRole(AccountApiTestHost factory, Guid accountId, short role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        (await db.Accounts.SingleAsync(a => a.Id == accountId)).RoleId = role;
        await db.SaveChangesAsync();
    }
}
