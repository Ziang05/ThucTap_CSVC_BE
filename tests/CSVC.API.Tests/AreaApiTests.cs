using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using CSVC.Core.Areas;
using CSVC.Core.Common;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Areas.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class AreaApiTests
{
    private const string Root = "/api/v1/areas";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Mapping_preserves_identity_and_parent_presence_distinguishes_omitted_from_null()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<AreaMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        mapper.ConfigurationProvider.AssertConfigurationIsValid();
        var parent = Guid.NewGuid();
        var target = new Area { Code = "KV_FIXED", Name = "Old", ParentId = parent, Status = EntityStatus.Locked };
        var id = target.Id;
        mapper.Map(new UpdateAreaRequest { Name = "  Tòa\t Một ", TypeLabel = " Tòa ", Description = " " }, target);
        Assert.Equal(id, target.Id);
        Assert.Equal("KV_FIXED", target.Code);
        Assert.Equal(parent, target.ParentId);
        Assert.Equal(EntityStatus.Locked, target.Status);
        Assert.Equal("Tòa Một", target.Name);
        Assert.Equal("Tòa", target.TypeLabel);
        Assert.Null(target.Description);
        Assert.False(JsonSerializer.Deserialize<UpdateAreaRequest>("{\"name\":\"A\"}", Json)!.HasParentChange());
        var explicitNull = JsonSerializer.Deserialize<UpdateAreaRequest>("{\"name\":\"A\",\"parentId\":null}", Json)!;
        Assert.True(explicitNull.HasParentChange());
        Assert.Null(explicitNull.ParentId);
    }

    [PostgreSqlFact]
    public async Task Create_roots_and_children_normalizes_names_and_enforces_sibling_uniqueness()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("  Tòa\t Một ".Normalize(NormalizationForm.FormD));
        Assert.Equal("Tòa Một", a.Name);
        Assert.Null(a.ParentId);
        Assert.Equal(32, a.Code.Length);
        Assert.StartsWith("KV", a.Code);
        Assert.Equal(new[] { a.Id }, a.PathIds);
        Assert.Equal(EntityStatus.Active, a.Status);
        Assert.Equal(EntityStatus.Active, a.EffectiveStatus);
        Assert.Equal(env.ManagerId, a.Management!.CreatedBy);
        Assert.Equal(env.Factory.Clock.GetUtcNow(), a.Management.CreatedAt);
        var b = await env.CreateAreaAsync("Tòa Hai");
        var child = await env.CreateAreaAsync("Room", a.Id);
        var otherChild = await env.CreateAreaAsync("Room", b.Id);
        Assert.NotEqual(child.Code, otherChild.Code);
        Assert.Equal(new[] { a.Id, child.Id }, child.PathIds);
        Assert.Equal(1, child.Depth);
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = " TÒA MỘT " }), HttpStatusCode.Conflict, "AREA_NAME_CONFLICT");
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = " room ", parentId = a.Id }), HttpStatusCode.Conflict, "AREA_NAME_CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{child.Id}/lock", null)).StatusCode);
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = "ROOM", parentId = a.Id }), HttpStatusCode.Conflict, "AREA_NAME_CONFLICT");
        var audit = Assert.Single(await env.HistoryAsync(a.Id));
        Assert.Equal("AREA_CREATED", audit.ActionCode);
        Assert.Equal(AuditOutcome.Success, audit.Outcome);
        Assert.Equal(env.ManagerId, audit.ActorAccountId);
        Assert.Null(audit.BeforeData);
        Assert.Equal(a.Id, JsonDocument.Parse(audit.AfterData!).RootElement.GetProperty("pathIds")[0].GetGuid());
    }

    [PostgreSqlFact]
    public async Task All_roles_read_roots_children_and_search_paths_without_exposing_management()
    {
        await using var env = await Harness.CreateAsync();
        using var anonymous = env.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Root)).StatusCode);
        var empty = await env.Manager.GetFromJsonAsync<JsonElement>(Root);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var a = await env.CreateAreaAsync("Alpha");
        var b = await env.CreateAreaAsync("Beta");
        var child = await env.CreateAreaAsync("Nested", a.Id);
        var leaf = await env.CreateAreaAsync("Leaf", child.Id);
        var auditCount = await env.CountHistoryAsync();
        foreach (var role in new short[] { 1, 2, 3 })
        {
            await env.SetStaffRoleAsync(role);
            var roots = await env.Staff.GetFromJsonAsync<JsonElement>(Root);
            Assert.Equal(2, roots.GetProperty("totalCount").GetInt32());
            Assert.All(roots.GetProperty("items").EnumerateArray(), node => Assert.Equal(JsonValueKind.Null, node.GetProperty("parentId").ValueKind));
            var detail = await env.Staff.GetFromJsonAsync<JsonElement>($"{Root}/{a.Id}");
            Assert.Equal(role == 1, detail.TryGetProperty("management", out _));
            Assert.False(detail.TryGetProperty("devices", out _));
            Assert.Equal(1, detail.GetProperty("childCount").GetInt32());
            Assert.Equal(child.Id, detail.GetProperty("children").GetProperty("items")[0].GetProperty("id").GetGuid());
            var search = await env.Staff.GetFromJsonAsync<JsonElement>($"{Root}?search={leaf.Code.ToLowerInvariant()}");
            Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
            var result = search.GetProperty("items")[0];
            Assert.Equal(new[] { a.Id, child.Id, leaf.Id }, result.GetProperty("pathIds").EnumerateArray().Select(v => v.GetGuid()));
            Assert.False(result.TryGetProperty("management", out _));
            var children = await env.Staff.GetFromJsonAsync<JsonElement>($"{Root}/{child.Id}/children");
            Assert.Equal(leaf.Id, children.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.Equal(role == 1 ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await env.Staff.GetAsync($"{Root}/{a.Id}/history")).StatusCode);
            Assert.Equal(role == 1 ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await env.Staff.GetAsync($"{Root}/{a.Id}/lock-impact")).StatusCode);
        }
        Assert.Equal(0, (await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}?search=not-found")).GetProperty("totalCount").GetInt32());
        Assert.Equal(auditCount, await env.CountHistoryAsync());
        Assert.Equal(b.Id, (await env.GetAreaAsync(b.Id)).Id);
    }

    [PostgreSqlFact]
    public async Task Reparenting_keeps_identifiers_updates_descendant_paths_and_preserves_old_snapshots()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        var b = await env.CreateAreaAsync("B");
        var child = await env.CreateAreaAsync("Child", a.Id);
        var leaf = await env.CreateAreaAsync("Leaf", child.Id);
        var oldLeafHistory = (await env.HistoryAsync(leaf.Id))[0].AfterData;
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "Renamed" })).StatusCode);
        Assert.Equal(a.Id, (await env.GetAreaAsync(child.Id)).ParentId);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}",
            new { name = "Moved", parentId = b.Id, typeLabel = " Any level ", description = " New " })).StatusCode);
        var moved = await env.GetAreaAsync(child.Id);
        var movedLeaf = await env.GetAreaAsync(leaf.Id);
        Assert.Equal(child.Code, moved.Code);
        Assert.Equal(new[] { b.Id, child.Id }, moved.PathIds);
        Assert.Equal(new[] { b.Id, child.Id, leaf.Id }, movedLeaf.PathIds);
        Assert.Equal(new[] { "B", "Moved", "Leaf" }, movedLeaf.PathNames);
        Assert.Equal(child.Id, movedLeaf.ParentId);
        Assert.Equal(leaf.Code, movedLeaf.Code);
        var history = await env.HistoryAsync(child.Id);
        var reparent = Assert.Single(history, h => h.ActionCode == "AREA_REPARENTED");
        var fields = JsonSerializer.Deserialize<string[]>(reparent.ChangedFields!)!;
        Assert.Equal(new[] { "name", "typeLabel", "description", "parentId" }, fields);
        Assert.Equal(a.Id, JsonDocument.Parse(reparent.BeforeData!).RootElement.GetProperty("parentId").GetGuid());
        Assert.Equal(b.Id, JsonDocument.Parse(reparent.AfterData!).RootElement.GetProperty("parentId").GetGuid());
        Assert.Equal(1, JsonDocument.Parse(reparent.AfterData!).RootElement.GetProperty("affectedDescendantCount").GetInt32());
        Assert.Equal(oldLeafHistory, Assert.Single(await env.HistoryAsync(leaf.Id)).AfterData);
        // Omitted parent keeps B, and sending identical content adds no event.
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}",
            new { name = "Moved", typeLabel = "Any level", description = "New" })).StatusCode);
        Assert.Equal(history.Count, (await env.HistoryAsync(child.Id)).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}",
            new { name = "Moved", parentId = (Guid?)null, typeLabel = "Any level", description = "New" })).StatusCode);
        Assert.Null((await env.GetAreaAsync(child.Id)).ParentId);
        Assert.Equal(new[] { child.Id, leaf.Id }, (await env.GetAreaAsync(leaf.Id)).PathIds);
    }

    [PostgreSqlFact]
    public async Task Cycles_invalid_parents_and_destination_name_conflicts_leave_the_tree_unchanged()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        var b = await env.CreateAreaAsync("B");
        var child = await env.CreateAreaAsync("Same", a.Id);
        var leaf = await env.CreateAreaAsync("Leaf", child.Id);
        var duplicate = await env.CreateAreaAsync("Same", b.Id);
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{a.Id}", new { name = "A", parentId = a.Id }), HttpStatusCode.Conflict, "AREA_CYCLE");
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{a.Id}", new { name = "A", parentId = leaf.Id }), HttpStatusCode.Conflict, "AREA_CYCLE");
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "same", parentId = b.Id }), HttpStatusCode.Conflict, "AREA_NAME_CONFLICT");
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{duplicate.Id}/lock", null)).StatusCode);
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "same", parentId = b.Id }), HttpStatusCode.Conflict, "AREA_NAME_CONFLICT");
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = "X", parentId = Guid.NewGuid() }), HttpStatusCode.NotFound, "AREA_PARENT_NOT_FOUND");
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = "X", parentId = Guid.Empty }), HttpStatusCode.BadRequest, "INVALID_AREA_PARENT");
        await env.CreateAreaAsync("Same");
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "same", parentId = (Guid?)null }), HttpStatusCode.Conflict, "AREA_NAME_CONFLICT");
        Assert.Null((await env.GetAreaAsync(a.Id)).ParentId);
        Assert.Equal(a.Id, (await env.GetAreaAsync(child.Id)).ParentId);
        Assert.Equal(new[] { a.Id, child.Id, leaf.Id }, (await env.GetAreaAsync(leaf.Id)).PathIds);
        Assert.DoesNotContain(await env.HistoryAsync(a.Id), h => h.ActionCode == "AREA_REPARENTED");
    }

    [PostgreSqlFact]
    public async Task Own_and_effective_locks_remain_distinct_and_do_not_rewrite_child_states()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        var b = await env.CreateAreaAsync("B");
        var child = await env.CreateAreaAsync("Child", a.Id);
        var leaf = await env.CreateAreaAsync("Leaf", child.Id);
        var impact = await env.Manager.GetFromJsonAsync<AreaLockImpactDto>($"{Root}/{a.Id}/lock-impact", Json);
        Assert.Equal(1, impact!.ChildCount);
        Assert.Equal(2, impact.DescendantCount);
        Assert.Null(impact.DirectDeviceCount);
        Assert.Null(impact.SubtreeDeviceCount);
        Assert.Null(impact.RequestCount);
        Assert.False(impact.LinkCountsComplete);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
        var inherited = await env.GetAreaAsync(leaf.Id);
        Assert.Equal(EntityStatus.Active, inherited.Status);
        Assert.Equal(EntityStatus.Locked, inherited.EffectiveStatus);
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = "New", parentId = child.Id }), HttpStatusCode.Conflict, "AREA_PARENT_UNAVAILABLE");
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{b.Id}", new { name = "B", parentId = child.Id }), HttpStatusCode.Conflict, "AREA_PARENT_UNAVAILABLE");
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{a.Id}", new { name = "A new", parentId = b.Id }), HttpStatusCode.Conflict, "AREA_LOCKED");
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{child.Id}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.DeleteAsync($"{Root}/{child.Id}/lock")).StatusCode);
        Assert.Equal(EntityStatus.Locked, (await env.GetAreaAsync(child.Id)).EffectiveStatus);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.DeleteAsync($"{Root}/{a.Id}/lock")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.DeleteAsync($"{Root}/{a.Id}/lock")).StatusCode);
        Assert.Equal(EntityStatus.Active, (await env.GetAreaAsync(leaf.Id)).EffectiveStatus);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{child.Id}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.DeleteAsync($"{Root}/{a.Id}/lock")).StatusCode);
        Assert.Equal(EntityStatus.Locked, (await env.GetAreaAsync(child.Id)).Status);
        Assert.Equal(EntityStatus.Locked, (await env.GetAreaAsync(leaf.Id)).EffectiveStatus);
        Assert.Single(await env.HistoryAsync(leaf.Id));
        Assert.Equal(HttpStatusCode.OK, (await env.Staff.GetAsync("/api/v1/accounts/me")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task Active_nodes_can_move_out_of_an_unavailable_branch_but_locked_nodes_cannot()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        var b = await env.CreateAreaAsync("B");
        var child = await env.CreateAreaAsync("Child", a.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
        await CodeAsync(await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "Changed" }), HttpStatusCode.Conflict, "AREA_PARENT_UNAVAILABLE");
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "Child", parentId = b.Id })).StatusCode);
        Assert.Equal(EntityStatus.Active, (await env.GetAreaAsync(child.Id)).EffectiveStatus);
    }

    [PostgreSqlFact]
    public async Task Validation_permissions_and_delete_denials_are_audited_without_fake_snapshots()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        foreach (var body in new object[] { new { name = " " }, new { name = new string('X', 201) },
            new { name = "X", typeLabel = new string('X', 101) }, new { name = "X", description = new string('X', 4001) },
            new { name = "X", code = "forged" }, new { name = "X", status = "Locked" }, new { name = "X", departmentId = Guid.NewGuid() } })
            Assert.Equal(HttpStatusCode.BadRequest, (await env.Manager.PostAsJsonAsync(Root, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Manager.PutAsJsonAsync($"{Root}/{a.Id}", new { name = "X", parentSpecified = true })).StatusCode);
        foreach (var role in new short[] { 2, 3 })
        {
            await env.SetStaffRoleAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await env.Staff.PostAsJsonAsync(Root, new { name = "Forbidden" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await env.Staff.PutAsJsonAsync($"{Root}/{a.Id}", new { name = "Forbidden" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await env.Staff.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await env.Staff.DeleteAsync($"{Root}/{a.Id}/lock")).StatusCode);
        }
        await CodeAsync(await env.Manager.DeleteAsync($"{Root}/{a.Id}"), HttpStatusCode.Forbidden, "AREA_DELETE_FORBIDDEN");
        await CodeAsync(await env.Staff.DeleteAsync($"{Root}/{a.Id}"), HttpStatusCode.Forbidden, "AREA_DELETE_FORBIDDEN");
        var missing = Guid.NewGuid();
        foreach (var url in new[] { $"{Root}/{missing}", $"{Root}/{missing}/children", $"{Root}/{missing}/history", $"{Root}/{missing}/lock-impact" })
            Assert.Equal(HttpStatusCode.NotFound, (await env.Manager.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Manager.PutAsync($"{Root}/{missing}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Manager.PutAsJsonAsync($"{Root}/{missing}", new { name = "X" })).StatusCode);
        await using var scope = env.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var failed = await db.AreaHistories.Where(h => h.Outcome != AuditOutcome.Success).ToListAsync();
        Assert.Equal(8, failed.Count(h => h.ReasonCode == "VALIDATION_FAILED"));
        Assert.Equal(8, failed.Count(h => h.ActionCode == "DENIED"));
        Assert.Equal(2, failed.Count(h => h.ActionCode == "DELETE_DENIED"));
        Assert.All(failed, h => { Assert.Null(h.BeforeData); Assert.Null(h.AfterData); Assert.Null(h.ChangedFields); });
        Assert.Equal(1, await db.Areas.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Pagination_sorting_status_filters_and_invalid_queries_are_read_only()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("Alpha");
        await env.CreateAreaAsync("Beta");
        var child = await env.CreateAreaAsync("Leaf", a.Id);
        await env.CreateAreaAsync("Second", a.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
        var history = await env.CountHistoryAsync();
        foreach (var sort in new[] { "Name", "Code" })
        foreach (var direction in new[] { "Asc", "Desc" })
        {
            var list = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}?sortBy={sort}&sortDirection={direction}&pageSize=1");
            Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
            Assert.Single(list.GetProperty("items").EnumerateArray());
            var page2 = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}?sortBy={sort}&sortDirection={direction}&pageSize=1&page=2");
            var key = char.ToLowerInvariant(sort[0]) + sort[1..];
            var values = new[] { list.GetProperty("items")[0].GetProperty(key).GetString(), page2.GetProperty("items")[0].GetProperty(key).GetString() };
            Assert.Equal(direction == "Asc" ? values.OrderBy(v => v, StringComparer.Ordinal) : values.OrderByDescending(v => v, StringComparer.Ordinal), values);
        }
        var inherited = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}?search=leaf&status=Active&effectiveStatus=Locked");
        Assert.Equal(child.Id, inherited.GetProperty("items")[0].GetProperty("id").GetGuid());
        var children = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{a.Id}/children?pageSize=1&page=2");
        Assert.Equal(2, children.GetProperty("totalCount").GetInt32());
        Assert.Single(children.GetProperty("items").EnumerateArray());
        foreach (var query in new[] { "page=0", "pageSize=101", "sortBy=999", "sortDirection=999", "status=999", "effectiveStatus=999" })
            Assert.Equal(HttpStatusCode.BadRequest, (await env.Manager.GetAsync($"{Root}?{query}")).StatusCode);
        Assert.Equal(history, await env.CountHistoryAsync());
    }

    [PostgreSqlFact]
    public async Task Recursive_paths_support_more_than_three_levels_without_nested_json_limits()
    {
        await using var env = await Harness.CreateAsync();
        Guid? parent = null;
        List<Guid> path = [];
        await using (var scope = env.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            for (var i = 0; i < 80; i++)
            {
                var area = new Area { Code = "DEPTH_" + i, Name = "Level " + i, ParentId = parent };
                db.Areas.Add(area);
                await db.SaveChangesAsync();
                path.Add(area.Id);
                parent = area.Id;
            }
        }
        var leaf = await env.GetAreaAsync(parent!.Value);
        Assert.Equal(79, leaf.Depth);
        Assert.Equal(path, leaf.PathIds);
        Assert.Equal(80, leaf.PathNames.Length);
        Assert.Equal(0, leaf.ChildCount);
        Assert.Empty(leaf.Children.Items);
        Assert.Null(leaf.Management!.CreatedAt);
        Assert.Null(leaf.Management.CreatedBy);
        Assert.Equal(0, await env.CountHistoryAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_duplicate_creations_opposing_moves_and_locks_are_serialized()
    {
        await using var env = await Harness.CreateAsync();
        var roots = await Task.WhenAll(env.Manager.PostAsJsonAsync(Root, new { name = "Root" }), env.Manager.PostAsJsonAsync(Root, new { name = " ROOT " }));
        Assert.Single(roots, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(roots, r => r.StatusCode == HttpStatusCode.Conflict);
        var a = await ReadAsync<AreaDetailDto>(roots.Single(r => r.StatusCode == HttpStatusCode.Created));
        var b = await env.CreateAreaAsync("B");
        var children = await Task.WhenAll(env.Manager.PostAsJsonAsync(Root, new { name = "Child", parentId = a.Id }),
            env.Manager.PostAsJsonAsync(Root, new { name = " child ", parentId = a.Id }));
        Assert.Single(children, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(children, r => r.StatusCode == HttpStatusCode.Conflict);
        var moves = await Task.WhenAll(env.Manager.PutAsJsonAsync($"{Root}/{a.Id}", new { name = a.Name, parentId = b.Id }),
            env.Manager.PutAsJsonAsync($"{Root}/{b.Id}", new { name = b.Name, parentId = a.Id }));
        Assert.Single(moves, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(moves, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var id in new[] { a.Id, b.Id })
        {
            var node = await env.GetAreaAsync(id);
            Assert.Equal(node.PathIds.Length, node.PathIds.Distinct().Count());
        }
        var locks = await Task.WhenAll(env.Manager.PutAsync($"{Root}/{a.Id}/lock", null), env.Manager.PutAsync($"{Root}/{a.Id}/lock", null));
        Assert.All(locks, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Single(await env.HistoryAsync(a.Id), h => h.ActionCode == "AREA_LOCKED");
    }

    [PostgreSqlFact]
    public async Task Parent_lock_racing_with_child_creation_has_a_valid_serial_outcome()
    {
        await using var env = await Harness.CreateAsync();
        var parent = await env.CreateAreaAsync("Parent");
        var creating = env.Manager.PostAsJsonAsync(Root, new { name = "Race", parentId = parent.Id });
        var locking = env.Manager.PutAsync($"{Root}/{parent.Id}/lock", null);
        await Task.WhenAll(creating, locking);
        Assert.Equal(HttpStatusCode.NoContent, locking.Result.StatusCode);
        Assert.Contains(creating.Result.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        if (creating.Result.StatusCode == HttpStatusCode.Created)
        {
            var created = await ReadAsync<AreaDetailDto>(creating.Result);
            Assert.Equal(EntityStatus.Active, (await env.GetAreaAsync(created.Id)).Status);
            Assert.Equal(EntityStatus.Locked, (await env.GetAreaAsync(created.Id)).EffectiveStatus);
        }
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = "Later", parentId = parent.Id }), HttpStatusCode.Conflict, "AREA_PARENT_UNAVAILABLE");
    }

    [PostgreSqlFact]
    public async Task Direct_sql_tree_writer_is_observed_after_waiting_for_the_existing_trigger_lock()
    {
        await using var env = await Harness.CreateAsync();
        var parent = await env.CreateAreaAsync("Parent");
        await using var connection = new NpgsqlConnection(env.Database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("UPDATE areas SET status = 'LOCKED' WHERE id = @id", connection, transaction))
        {
            command.Parameters.AddWithValue("id", parent.Id);
            await command.ExecuteNonQueryAsync();
        }
        var creating = env.Manager.PostAsJsonAsync(Root, new { name = "Must not be inserted", parentId = parent.Id });
        // Wait until the API actually blocks on the DB tree lock, rather than relying on scheduling.
        var timer = Stopwatch.StartNew();
        var waiting = false;
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND NOT granted
                    AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))
                """, connection, transaction);
            if ((bool)(await command.ExecuteScalarAsync())!) { waiting = true; break; }
            await Task.Delay(20);
        }
        await transaction.CommitAsync();
        var response = await creating;
        Assert.True(waiting, "The API did not wait for the PostgreSQL area-tree lock.");
        await CodeAsync(response, HttpStatusCode.Conflict, "AREA_PARENT_UNAVAILABLE");
        await using var scope = env.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CsvcDbContext>().Areas.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Audit_failures_roll_back_create_update_reparent_lock_and_unlock()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        var b = await env.CreateAreaAsync("B");
        var child = await env.CreateAreaAsync("Child", a.Id);
        var leaf = await env.CreateAreaAsync("Leaf", child.Id);
        foreach (var action in new[] { "AREA_CREATED", "AREA_UPDATED", "AREA_REPARENTED", "AREA_LOCKED", "AREA_UNLOCKED" })
        {
            if (action == "AREA_UNLOCKED") Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{child.Id}/lock", null)).StatusCode);
            env.Factory.Failures.FailNextAreaAction = action;
            var response = action switch
            {
                "AREA_CREATED" => await env.Manager.PostAsJsonAsync(Root, new { name = "Rollback" }),
                "AREA_UPDATED" => await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "Changed" }),
                "AREA_REPARENTED" => await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "Changed", parentId = b.Id }),
                "AREA_LOCKED" => await env.Manager.PutAsync($"{Root}/{child.Id}/lock", null),
                _ => await env.Manager.DeleteAsync($"{Root}/{child.Id}/lock")
            };
            await CodeAsync(response, HttpStatusCode.ServiceUnavailable, "AREA_UNAVAILABLE");
            var after = await env.GetAreaAsync(child.Id);
            Assert.Equal("Child", after.Name);
            Assert.Equal(a.Id, after.ParentId);
            Assert.Equal(action == "AREA_UNLOCKED" ? EntityStatus.Locked : EntityStatus.Active, after.Status);
            Assert.Equal(new[] { a.Id, child.Id, leaf.Id }, (await env.GetAreaAsync(leaf.Id)).PathIds);
        }
        Assert.Single(await env.HistoryAsync(leaf.Id));
        await using var scope = env.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(4, await db.Areas.CountAsync());
        Assert.Equal(5, await db.AreaHistories.CountAsync(h => h.ReasonCode == "AREA_UNAVAILABLE"));
        Assert.False(await db.AreaHistories.AnyAsync(h => h.ActionCode == "AREA_REPARENTED" || h.ActionCode == "AREA_UPDATED" || h.ActionCode == "AREA_UNLOCKED"));
    }

    [PostgreSqlFact]
    public async Task History_is_read_only_scoped_to_current_branch_and_handles_audit_storage_failure()
    {
        await using var env = await Harness.CreateAsync();
        var a = await env.CreateAreaAsync("A");
        var child = await env.CreateAreaAsync("Child", a.Id);
        env.Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsync($"{Root}/{a.Id}/lock", null)).StatusCode);
        var own = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{a.Id}/history?pageSize=1");
        Assert.Equal(2, own.GetProperty("totalCount").GetInt32());
        Assert.Equal("AREA_LOCKED", own.GetProperty("items")[0].GetProperty("actionCode").GetString());
        var branch = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{a.Id}/history?includeDescendants=true");
        Assert.Equal(3, branch.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await env.Manager.PutAsJsonAsync($"{Root}/{child.Id}", new { name = "Child", parentId = (Guid?)null })).StatusCode);
        Assert.Equal(2, (await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{a.Id}/history?includeDescendants=true")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Manager.GetAsync($"{Root}/{a.Id}/history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await env.Manager.DeleteAsync($"{Root}/{a.Id}/history")).StatusCode);
        env.Factory.Failures.FailNextAreaAction = "FAILED";
        await CodeAsync(await env.Manager.PostAsJsonAsync(Root, new { name = "A" }), HttpStatusCode.ServiceUnavailable, "AREA_UNAVAILABLE");
        Assert.Equal(4, await env.CountHistoryAsync());
    }

    [PostgreSqlFact]
    public async Task History_items_include_current_actor_name_and_keep_events_without_an_actor()
    {
        await using var env = await Harness.CreateAsync();
        var area = await env.CreateAreaAsync("Phòng thí nghiệm");
        var original = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{area.Id}/history");
        var created = Assert.Single(original.GetProperty("items").EnumerateArray());
        Assert.Equal(env.ManagerId, created.GetProperty("actorAccountId").GetGuid());
        Assert.Equal("TK_MANAGER", created.GetProperty("actorAccountCode").GetString());
        Assert.Equal("Test Manager", created.GetProperty("actorFullName").GetString());
        Assert.False(created.TryGetProperty("afterData", out _));
        var detailPath = $"{Root}/{area.Id}/history/{created.GetProperty("id").GetGuid()}";
        var snapshot = (await env.Manager.GetFromJsonAsync<JsonElement>(detailPath)).GetProperty("afterData").GetString();
        var systemEvent = new AreaHistory
        {
            RequestId = Guid.NewGuid(), AreaId = area.Id, ActionCode = "FAILED",
            Outcome = AuditOutcome.Failed, ReasonCode = "VALIDATION_FAILED",
            OccurredAt = env.Factory.Clock.GetUtcNow().AddMinutes(1)
        };
        await using (var scope = env.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var manager = await db.Accounts.SingleAsync(a => a.Id == env.ManagerId);
            (await db.UserProfiles.SingleAsync(p => p.Id == manager.UserProfileId)).FullName = "Nguyễn Đình Bảo";
            db.AreaHistories.Add(systemEvent);
            await db.SaveChangesAsync();
        }
        var firstPage = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{area.Id}/history?pageSize=1");
        Assert.Equal(2, firstPage.GetProperty("totalCount").GetInt32());
        var anonymous = Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        Assert.Equal(systemEvent.Id, anonymous.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, anonymous.GetProperty("actorAccountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, anonymous.GetProperty("actorAccountCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, anonymous.GetProperty("actorFullName").ValueKind);
        var secondPage = await env.Manager.GetFromJsonAsync<JsonElement>($"{Root}/{area.Id}/history?page=2&pageSize=1");
        Assert.Equal(2, secondPage.GetProperty("totalCount").GetInt32());
        var renamed = Assert.Single(secondPage.GetProperty("items").EnumerateArray());
        Assert.Equal(created.GetProperty("id").GetGuid(), renamed.GetProperty("id").GetGuid());
        Assert.Equal("Nguyễn Đình Bảo", renamed.GetProperty("actorFullName").GetString());
        Assert.False(renamed.TryGetProperty("afterData", out _));
        Assert.Equal(snapshot, (await env.Manager.GetFromJsonAsync<JsonElement>(detailPath)).GetProperty("afterData").GetString());
        Assert.Equal(2, await env.CountHistoryAsync());
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Json))!;
    private static async Task CodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await ReadAsync<JsonElement>(response)).GetProperty("code").GetString());
    }

    private sealed class Harness(AccountTestDatabase database, AccountApiTestHost factory,
        (Guid Manager, string ManagerToken, Guid Staff, string StaffToken, Guid Department) seed) : IAsyncDisposable
    {
        public AccountTestDatabase Database { get; } = database;
        public AccountApiTestHost Factory { get; } = factory;
        public Guid ManagerId => seed.Manager;
        public HttpClient Manager { get; } = Client(factory, seed.ManagerToken);
        public HttpClient Staff { get; } = Client(factory, seed.StaffToken);

        public static async Task<Harness> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var factory = new AccountApiTestHost(database.ConnectionString);
            return new(database, factory, await factory.SeedAsync());
        }
        public async Task<AreaDetailDto> CreateAreaAsync(string name, Guid? parentId = null)
        {
            var response = await Manager.PostAsJsonAsync(Root, new { name, parentId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await ReadAsync<AreaDetailDto>(response);
            Assert.EndsWith(result.Id.ToString(), response.Headers.Location!.ToString());
            var audit = Assert.Single(await HistoryAsync(result.Id));
            Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), audit.RequestId.ToString());
            return result;
        }
        public async Task<AreaDetailDto> GetAreaAsync(Guid id) => (await Manager.GetFromJsonAsync<AreaDetailDto>($"{Root}/{id}", Json))!;
        public async Task<List<AreaHistory>> HistoryAsync(Guid id)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CsvcDbContext>().AreaHistories.AsNoTracking().Where(h => h.AreaId == id).ToListAsync();
        }
        public async Task<int> CountHistoryAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CsvcDbContext>().AreaHistories.CountAsync();
        }
        public async Task SetStaffRoleAsync(short role)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            (await db.Accounts.SingleAsync(a => a.Id == seed.Staff)).RoleId = role;
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Manager.Dispose(); Staff.Dispose();
            await Factory.DisposeAsync();
            await Database.DisposeAsync();
        }
        private static HttpClient Client(AccountApiTestHost factory, string token)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}
