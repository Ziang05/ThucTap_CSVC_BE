using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed partial class SupplierApiTests
{
    [PostgreSqlFact]
    public async Task Contact_fields_validate_normalize_and_are_hidden_from_department_staff()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        foreach (var body in new object[] {
            new { name = "Invalid", taxCode = "010 123" }, new { name = "Invalid", taxCode = "ABC--123" },
            new { name = "Invalid", taxCode = new string('A', 51) }, new { name = "Invalid", phone = "CALL ME" },
            new { name = "Invalid", phone = new string('1', 33) }, new { name = "Invalid", email = "not-an-email" },
            new { name = "Invalid", email = new string('a', 315) + "@example.com" }, new { name = "Invalid", address = new string('a', 4001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(Root, body)).StatusCode);
        var response = await manager.PostAsJsonAsync(Root, new { name = "Contact supplier", displayCode = "NCC_CONTACT", taxCode = " ab123-001 ",
            address = "  123 Nguyễn Trãi  ", phone = " +84 (28) 1234-5678 ", email = " CONTACT@EXAMPLE.COM ", notes = "Internal" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Read<JsonElement>(response);
        Assert.Equal("AB123-001", created.GetProperty("taxCode").GetString());
        Assert.Equal("123 Nguyễn Trãi", created.GetProperty("address").GetString());
        Assert.Equal("+84 (28) 1234-5678", created.GetProperty("phone").GetString());
        Assert.Equal("contact@example.com", created.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Root}/NCC_CONTACT")).StatusCode);
        var privileged = await staff.GetFromJsonAsync<JsonElement>($"{Root}/NCC_CONTACT");
        Assert.Equal("AB123-001", privileged.GetProperty("taxCode").GetString());
        await ChangeRole(factory, seed.Staff, 3);
        var basic = await staff.GetFromJsonAsync<JsonElement>($"{Root}/NCC_CONTACT");
        foreach (var field in new[] { "id", "taxCode", "address", "phone", "email", "notes", "management", "version" })
            Assert.False(basic.TryGetProperty(field, out _));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(1, await db.Suppliers.CountAsync());
        Assert.Equal(8, await db.SupplierHistories.CountAsync(h => h.ReasonCode == "VALIDATION_FAILED"));
    }

    [PostgreSqlFact]
    public async Task Tax_codes_are_unique_including_locked_suppliers_and_contact_diffs_are_exact()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.StaffToken);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Root, new { name = "One", displayCode = "NCC_ONE", taxCode = " ab123 ",
            address = "Old address", phone = "0123456789", email = "old@example.com", notes = "Old note" })).StatusCode);
        await Code(await client.PostAsJsonAsync(Root, new { name = "Duplicate", taxCode = "AB123" }), HttpStatusCode.Conflict, "SUPPLIER_TAX_CODE_CONFLICT");
        await client.PutAsync($"{Root}/NCC_ONE/lock", null);
        var target = await Create(client, "Two", "NCC_TWO");
        await Code(await client.PutAsJsonAsync($"{Root}/{target.DisplayCode}", new { name = target.Name, taxCode = "ab123" }), HttpStatusCode.Conflict, "SUPPLIER_TAX_CODE_CONFLICT");
        await client.DeleteAsync($"{Root}/NCC_ONE/lock");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/NCC_ONE", new { name = "One", taxCode = "AB123",
            address = "New address", phone = "+84 912345678", email = "new@example.com", notes = "New note" })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var updated = await db.SupplierHistories.SingleAsync(h => h.ActionCode == "SUPPLIER_UPDATED" && h.Outcome == AuditOutcome.Success);
            Assert.Equal(new[] { "address", "phone", "email", "notes" }, JsonSerializer.Deserialize<string[]>(updated.ChangedFields!));
            Assert.Equal("AB123", (await db.Suppliers.SingleAsync(s => s.DisplayCode == "NCC_ONE")).TaxCode);
        }
        // Optional fields omitted in PUT are cleared, and the released tax code can be reused.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/NCC_ONE", new { name = "One" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Root, new { name = "Reused", taxCode = "AB123" })).StatusCode);
        var cleared = await client.GetFromJsonAsync<JsonElement>($"{Root}/NCC_ONE");
        foreach (var field in new[] { "taxCode", "address", "phone", "email", "notes" }) Assert.False(cleared.TryGetProperty(field, out _));
        await Create(client, "No tax one");
        await Create(client, "No tax two");
    }

    [PostgreSqlFact]
    public async Task Concurrent_normalized_tax_codes_create_one_supplier_and_database_rejects_bypasses()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        var results = await Task.WhenAll(client.PostAsJsonAsync(Root, new { name = "Race A", taxCode = " abc-123 " }),
            client.PostAsJsonAsync(Root, new { name = "Race B", taxCode = "ABC-123" }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        await Code(Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "SUPPLIER_TAX_CODE_CONFLICT");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        db.Suppliers.Add(new Supplier { DisplayCode = "NCC_BYPASS", Name = "Bypass", TaxCode = " AbC-123 " });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("uq_suppliers_normalized_tax_code", Assert.IsType<PostgresException>(failure.InnerException).ConstraintName);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Suppliers.CountAsync());
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.Outcome == AuditOutcome.Success));
        Assert.Equal(1, await db.SupplierHistories.CountAsync(h => h.ReasonCode == "SUPPLIER_TAX_CODE_CONFLICT"));
    }

    [PostgreSqlFact]
    public async Task Tax_code_migration_preserves_existing_suppliers_histories_and_other_modules()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261023171137_AddCatalogModules");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO suppliers(id, display_code, name, tax_code) VALUES ({id}, 'LEGACY', 'Legacy supplier', ' abc-123 ')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO suppliers(id, display_code, name, tax_code) VALUES (gen_random_uuid(), 'EMPTY1', 'Empty one', NULL), (gen_random_uuid(), 'EMPTY2', 'Empty two', '  ')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO supplier_history(id, request_id, action_code, outcome, occurred_at, supplier_id) VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, 'SUPPLIER_CREATED', 'SUCCESS', {DateTimeOffset.UtcNow}, {id})");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO device_categories(id, display_code, name) VALUES (gen_random_uuid(), 'DM_LEGACY', 'Existing category')");
        var roleCount = await db.Roles.CountAsync();
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(3, await db.Suppliers.CountAsync());
        var stored = await db.Suppliers.SingleAsync(s => s.Id == id);
        Assert.Equal(" abc-123 ", stored.TaxCode);
        Assert.Equal("ABC-123", db.Entry(stored).Property<string>("NormalizedTaxCode").CurrentValue);
        Assert.Equal(id, (await db.SupplierHistories.SingleAsync()).SupplierId);
        Assert.Equal(1, await db.DeviceCategories.CountAsync());
        Assert.Equal(roleCount, await db.Roles.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Tax_code_migration_with_legacy_duplicates_fails_without_deleting_or_rewriting_data()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261023171137_AddCatalogModules");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO suppliers(id, display_code, name, tax_code) VALUES (gen_random_uuid(), 'OLD1', 'Old one', 'abc-123'), (gen_random_uuid(), 'OLD2', 'Old two', ' ABC-123 ')");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM suppliers WHERE tax_code IN ('abc-123', ' ABC-123 ')").SingleAsync();
        Assert.Equal(2, count);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM information_schema.columns WHERE table_name = 'suppliers' AND column_name = 'normalized_tax_code'").SingleAsync());
        Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_UniqueSupplierTaxCode"));
    }
}
