using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.Manufacturers;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed partial class ManufacturerApiTests
{
    [PostgreSqlFact]
    public async Task Additional_fields_validate_normalize_and_follow_role_visibility()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var manager = Client(factory, seed.ManagerToken);
        using var staff = Client(factory, seed.StaffToken);
        foreach (var website in new[] { "example.com", "/relative", "javascript:alert(1)", "ftp://example.com", "file:///etc/hosts",
            "https://user:password@example.com", "https://", "https://bad host.example", "https://example.com/" + new string('a', 2048) })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(Root, new { name = "Invalid website", website })).StatusCode);
        foreach (var body in new object[] { new { name = "Invalid", country = new string('a', 121) },
            new { name = "Invalid", shortName = new string('a', 121) }, new { name = "Invalid", contactInfo = new string('a', 4001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(Root, body)).StatusCode);
        var response = await staff.PostAsJsonAsync(Root, new { name = "ACME", displayCode = "HSX_ACME",
            country = "  Việt\t Nam ".Normalize(NormalizationForm.FormD), website = " https://example.com/Products?Lang=vi ",
            shortName = " ACME\t Brand ", contactInfo = " Contact team ", notes = "Internal" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await Read<JsonElement>(response);
        Assert.Equal("Việt Nam", detail.GetProperty("country").GetString());
        Assert.Equal("https://example.com/Products?Lang=vi", detail.GetProperty("website").GetString());
        Assert.Equal("ACME Brand", detail.GetProperty("shortName").GetString());
        Assert.Equal("Contact team", detail.GetProperty("contactInfo").GetString());
        var managed = await manager.GetFromJsonAsync<JsonElement>($"{Root}/HSX_ACME");
        Assert.Equal("Test Staff", managed.GetProperty("management").GetProperty("createdByFullName").GetString());
        await ChangeRole(factory, seed.Staff, 3);
        var basic = await staff.GetFromJsonAsync<JsonElement>($"{Root}/HSX_ACME");
        foreach (var field in new[] { "id", "country", "website", "shortName", "contactInfo", "notes", "version", "management" })
            Assert.False(basic.TryGetProperty(field, out _));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(1, await db.Manufacturers.CountAsync());
        Assert.Equal(12, await db.ManufacturerHistories.CountAsync(h => h.ReasonCode == "VALIDATION_FAILED"));
        Assert.All(await db.ManufacturerHistories.Where(h => h.Outcome == AuditOutcome.Failed).ToListAsync(), h => Assert.Null(h.AfterData));
    }

    [PostgreSqlFact]
    public async Task Manufacturer_field_updates_have_exact_diffs_clear_optional_values_and_audit_validation()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Root, new { name = "Original", displayCode = "HSX_DETAIL",
            country = "Vietnam", website = "http://example.com/Old", shortName = "OLD", contactInfo = "Old contact", notes = "Old notes" })).StatusCode);
        var initial = (await client.GetFromJsonAsync<UseCases.Manufacturers.DTOs.ManufacturerDetailDto>($"{Root}/HSX_DETAIL", Json))!;
        await Code(await client.PutAsJsonAsync($"{Root}/HSX_DETAIL", new { name = "Invalid change", website = "javascript:alert(1)" }),
            HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/HSX_DETAIL", new { name = "Original", country = "Japan",
            website = "https://example.com/New", shortName = "NEW", contactInfo = "New contact", notes = "New notes", version = initial.Version })).StatusCode);
        var changed = (await client.GetFromJsonAsync<UseCases.Manufacturers.DTOs.ManufacturerDetailDto>($"{Root}/HSX_DETAIL", Json))!;
        Assert.Equal(initial.DisplayCode, changed.DisplayCode);
        Assert.NotEqual(initial.Version, changed.Version);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var updated = await db.ManufacturerHistories.SingleAsync(h => h.ActionCode == "MANUFACTURER_UPDATED" && h.Outcome == AuditOutcome.Success);
            Assert.Equal(new[] { "country", "website", "short_name", "contact_info", "notes" }, JsonSerializer.Deserialize<string[]>(updated.ChangedFields!));
            Assert.Equal(1, await db.ManufacturerHistories.CountAsync(h => h.ActionCode == "MANUFACTURER_UPDATED" && h.ReasonCode == "VALIDATION_FAILED"));
            Assert.Equal("Japan", (await db.Manufacturers.SingleAsync()).Country);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/HSX_DETAIL", new { name = "Original" })).StatusCode);
        var empty = await client.GetFromJsonAsync<JsonElement>($"{Root}/HSX_DETAIL");
        foreach (var field in new[] { "country", "website", "shortName", "contactInfo", "notes" }) Assert.False(empty.TryGetProperty(field, out _));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/HSX_DETAIL", new { name = "Original", country = " ", website = " ", shortName = " ", contactInfo = " " })).StatusCode);
        await using var readScope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await readScope.ServiceProvider.GetRequiredService<CsvcDbContext>().ManufacturerHistories.CountAsync(h => h.ActionCode == "MANUFACTURER_UPDATED" && h.Outcome == AuditOutcome.NoChange));
    }

    [PostgreSqlFact]
    public async Task Country_website_and_brand_are_not_unique_keys_and_creation_does_not_create_other_modules()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.StaffToken);
        foreach (var name in new[] { "First manufacturer", "Second manufacturer" })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Root, new { name, country = "Vietnam",
                website = "https://example.com", shortName = "Same brand", contactInfo = "Shared contact" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        Assert.Equal(2, await db.Manufacturers.CountAsync());
        Assert.Equal(2, await db.Manufacturers.Select(m => m.DisplayCode).Distinct().CountAsync());
        Assert.False(await db.Suppliers.AnyAsync());
        Assert.False(await db.DeviceCategories.AnyAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(2, await db.ManufacturerHistories.CountAsync(h => h.ActionCode == "MANUFACTURER_CREATED" && h.Outcome == AuditOutcome.Success));
    }

    [PostgreSqlFact]
    public async Task Manufacturer_history_keeps_allowed_extra_fields_and_rejects_unsafe_or_oversized_values()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        Guid safeId, unsafeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var safe = new ManufacturerHistory { RequestId = Guid.NewGuid(), ActionCode = "MANUFACTURER_UPDATED", Outcome = AuditOutcome.Success,
                OccurredAt = factory.Clock.GetUtcNow(), BeforeData = "{\"display_code\":\"HSX_IMPORT\",\"country\":\"Vietnam\",\"website\":\"https://old.example.com\",\"short_name\":\"ACME\",\"contact_info\":\"Old contact\"}",
                AfterData = "{\"display_code\":\"HSX_IMPORT\",\"country\":\"Vietnam\",\"website\":\"https://new.example.com\",\"short_name\":\"ACME\",\"contact_info\":\"New contact\",\"token\":\"SECRET\"}", ChangedFields = "[\"token\"]" };
            var unsafeEvent = new ManufacturerHistory { RequestId = Guid.NewGuid(), ActionCode = "MANUFACTURER_CREATED", Outcome = AuditOutcome.Success,
                OccurredAt = factory.Clock.GetUtcNow(), AfterData = JsonSerializer.Serialize(new { display_code = "HSX_UNSAFE",
                    website = "javascript:alert(1)", country = new string('x', 121), short_name = new[] { "nested" }, contact_info = new { token = "SECRET" } }) };
            safeId = safe.Id; unsafeId = unsafeEvent.Id;
            db.AddRange(safe, unsafeEvent);
            await db.SaveChangesAsync();
        }
        var safeDetail = await client.GetFromJsonAsync<JsonElement>($"{History}/{safeId}");
        Assert.Equal("Vietnam", safeDetail.GetProperty("afterData").GetProperty("country").GetString());
        Assert.Equal("https://new.example.com", safeDetail.GetProperty("afterData").GetProperty("website").GetString());
        Assert.Equal("ACME", safeDetail.GetProperty("afterData").GetProperty("short_name").GetString());
        Assert.Equal("New contact", safeDetail.GetProperty("afterData").GetProperty("contact_info").GetString());
        Assert.Equal(new[] { "website", "contact_info" }, safeDetail.GetProperty("changedFields").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("SECRET", safeDetail.GetRawText());
        var unsafeDetail = await client.GetFromJsonAsync<JsonElement>($"{History}/{unsafeId}");
        foreach (var field in new[] { "website", "country", "short_name", "contact_info" })
            Assert.False(unsafeDetail.GetProperty("afterData").TryGetProperty(field, out _));
        Assert.DoesNotContain("SECRET", unsafeDetail.GetRawText());
        Assert.DoesNotContain("javascript:", unsafeDetail.GetRawText());
    }
    [PostgreSqlFact]
    public async Task Clearing_a_legacy_invalid_website_records_the_change_without_exposing_the_old_URL()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = Client(factory, seed.ManagerToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            db.Manufacturers.Add(new Manufacturer { DisplayCode = "HSX_LEGACY", Name = "Legacy", Website = "javascript:alert(1)" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Root}/HSX_LEGACY", new { name = "Legacy", contactInfo = "New contact" })).StatusCode);
        Guid eventId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            Assert.Null((await db.Manufacturers.SingleAsync()).Website);
            var audit = await db.ManufacturerHistories.SingleAsync();
            Assert.Equal(AuditOutcome.Success, audit.Outcome);
            Assert.Equal(new[] { "website", "contact_info" }, JsonSerializer.Deserialize<string[]>(audit.ChangedFields!));
            eventId = audit.Id;
        }
        var detail = await client.GetFromJsonAsync<JsonElement>($"{History}/{eventId}");
        Assert.False(detail.GetProperty("beforeData").TryGetProperty("website", out _));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("afterData").GetProperty("website").ValueKind);
        Assert.Equal(new[] { "website", "contact_info" }, detail.GetProperty("changedFields").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("javascript:", detail.GetRawText());
    }

}
