using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Areas;
using CSVC.Core.Manufacturers;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceReceipts.DTOs;
using CSVC.UseCases.Devices.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class DeviceQuantityApiTests
{
    private const string Devices = "/api/v1/devices";
    private const string Receipts = "/api/v1/device-receipts";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [PostgreSqlFact]
    public async Task Full_device_form_can_register_then_receive_an_existing_device_without_losing_metadata()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            db.Add(new Manufacturer { DisplayCode = "MFR_FULL", Name = "Full form manufacturer" });
            db.Add(new Area { Code = "AREA_FULL", Name = "Full form area" });
            await db.SaveChangesAsync();
        });
        var parent = await f.CreateDevice();
        using var response = await f.Staff.PostAsJsonAsync(Devices, new CreateDeviceRequest
        {
            DeviceCode = "TB-FULL-FORM", Name = "Full form device", CategoryCode = "CAT_QTY", DepartmentCode = "PB_TEST",
            ResponsibleAccountCode = "TK_STAFF", ManufacturerCode = "MFR_FULL", Model = "Model full", SerialNumber = "SN-FULL",
            PurchaseDate = new DateOnly(2026, 10, 20), PaidInvoiceCode = "INV-FULL", PurchasePrice = 12345678.50m,
            WarrantyExpiryDate = new DateOnly(2028, 10, 20), AreaCode = "AREA_FULL", Notes = "Full metadata"
        }, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<DeviceDto>(Json))!;
        Assert.Equal(0, created.Quantity);
        await Expect(await f.Staff.PutAsJsonAsync(Devices + "/" + created.DeviceCode + "/parent",
            new { parentDeviceCode = parent.DeviceCode }), HttpStatusCode.NoContent);
        var registered = await f.GetDevice(created.DeviceCode);
        Assert.Equal(parent.DeviceCode, registered.ParentDeviceCode);
        var receipt = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = created.DeviceCode });
        await f.Approve(receipt.ReceiptCode);
        Assert.Equal(0, (await f.GetDevice(created.DeviceCode)).Quantity);
        await f.Complete(receipt.ReceiptCode);
        Assert.Equal(registered with { Quantity = 1, Availability = DeviceAvailability.Available }, await f.GetDevice(created.DeviceCode));
    }

    [PostgreSqlFact]
    public async Task Manual_creation_has_zero_quantity_on_every_read_and_cannot_override_it()
    {
        await using var f = await Fixture.CreateAsync();
        var device = await f.CreateDevice();
        Assert.Equal(0, device.Quantity);
        Assert.Equal(DeviceAvailability.NotReceived, device.Availability);
        Assert.Equal(0, (await f.GetDevice(device.DeviceCode)).Quantity);
        var scan = await f.Staff.GetFromJsonAsync<DeviceDto>(Devices + "/by-barcode/" + device.Barcode, Json);
        Assert.Equal(0, scan!.Quantity);
        var pending = await f.Staff.GetFromJsonAsync<PagedResult<DeviceDto>>(Devices + "?availability=NotReceived", Json);
        Assert.Equal(device.DeviceCode, Assert.Single(pending!.Items).DeviceCode);
        var available = await f.Staff.GetFromJsonAsync<PagedResult<DeviceDto>>(Devices + "?availability=Available", Json);
        Assert.Empty(available!.Items);
        await Expect(await f.Staff.PutAsJsonAsync(Devices + "/" + device.DeviceCode,
            new { name = "Changed", quantity = 1 }), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(Devices,
            new { name = "Other", categoryCode = "CAT_QTY", departmentCode = "PB_TEST", quantity = 1 }), HttpStatusCode.BadRequest);
        await f.InDb(async db =>
        {
            var settings = await db.EquipmentSettings.SingleAsync();
            settings.EquipmentDepartmentId = (await db.Departments.SingleAsync(d => d.Code == "PB_TEST")).Id;
            await db.SaveChangesAsync();
        });
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/service-orders", new
        {
            requestId = Guid.NewGuid(), orderType = "Maintenance",
            items = new[] { new { deviceCode = device.DeviceCode, executionMethod = "OnSite", workDescription = "Check" } }
        }), HttpStatusCode.Conflict, "DEVICE_NOT_RECEIVED");
    }

    [PostgreSqlFact]
    public async Task Mixed_receipt_links_existing_identity_and_creates_distinct_new_devices_only_on_completion()
    {
        await using var f = await Fixture.CreateAsync();
        var original = await f.CreateDevice();
        var receipt = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = original.DeviceCode },
            new DeviceReceiptItemRequest { Name = "Same model", CategoryCode = "CAT_QTY" },
            new DeviceReceiptItemRequest { Name = "Same model", CategoryCode = "CAT_QTY" });
        Assert.NotNull(receipt.Items);
        Assert.Equal(original.Barcode, receipt.Items.Single(x => x.DeviceCode == original.DeviceCode).Barcode);
        Assert.Equal(3, receipt.Items.Select(x => x.DeviceCode).Distinct().Count());
        Assert.All(receipt.Items, x => Assert.False(x.Completed));
        await f.InDb(async db => Assert.Single(await db.Devices.ToListAsync()));
        await f.Approve(receipt.ReceiptCode);
        Assert.Equal(0, (await f.GetDevice(original.DeviceCode)).Quantity);
        Assert.Equal(DeviceAvailability.Reserved, (await f.GetDevice(original.DeviceCode)).Availability);
        await f.Complete(receipt.ReceiptCode);
        foreach (var item in receipt.Items)
        {
            var device = await f.GetDevice(item.DeviceCode);
            Assert.Equal(1, device.Quantity);
            Assert.Equal(item.Barcode, device.Barcode);
            Assert.Equal(DeviceAvailability.Available, device.Availability);
        }
        await f.InDb(async db =>
        {
            Assert.Equal(3, await db.Devices.CountAsync());
            Assert.Equal(3, await db.DeviceIdentities.CountAsync());
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            var histories = await db.DeviceHistories.Where(x => x.ActionCode.StartsWith("DEVICE_RECEIVED_")).ToListAsync();
            Assert.Equal(3, histories.Count);
            Assert.All(histories, x => Assert.Equal(1, JsonDocument.Parse(x.AfterData!).RootElement.GetProperty("quantity").GetInt32()));
        });
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, f.Request(new DeviceReceiptItemRequest { ExistingDeviceCode = original.DeviceCode }), Json),
            HttpStatusCode.Conflict, "DEVICE_ALREADY_RECEIVED");
        await Expect(await f.Staff.PostAsync(Receipts + "/" + receipt.ReceiptCode + "/complete", null), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Pending_edits_cancel_and_reject_keep_zero_quantity_and_allow_another_receipt()
    {
        await using var f = await Fixture.CreateAsync();
        var device = await f.CreateDevice();
        var first = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode });
        await Expect(await f.Staff.PutAsJsonAsync(Receipts + "/" + first.ReceiptCode,
            f.Request(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode }, new DeviceReceiptItemRequest { Name = "New", CategoryCode = "CAT_QTY" }), Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsync(Receipts + "/" + first.ReceiptCode + "/cancel", null), HttpStatusCode.NoContent);
        var second = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode });
        await Expect(await f.Manager.PostAsJsonAsync(Receipts + "/" + second.ReceiptCode + "/reject", new { reason = "Later" }), HttpStatusCode.NoContent);
        Assert.Equal(0, (await f.GetDevice(device.DeviceCode)).Quantity);
        var third = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode });
        Assert.Equal(device.Barcode, Assert.Single(third.Items!).Barcode);
        await f.Approve(third.ReceiptCode);
        await f.Complete(third.ReceiptCode);
        Assert.Equal(1, (await f.GetDevice(device.DeviceCode)).Quantity);
    }

    [PostgreSqlFact]
    public async Task Competing_receipts_cannot_receive_the_same_device_twice()
    {
        await using var f = await Fixture.CreateAsync();
        var device = await f.CreateDevice();
        var first = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode });
        var second = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode });
        var responses = await Task.WhenAll(new[] { first, second }.Select(x =>
            f.Manager.PostAsJsonAsync(Receipts + "/" + x.ReceiptCode + "/approve", new { })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        await Expect(responses.Single(x => x.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.Conflict, "DEVICE_BUSY");
        var winner = responses[0].StatusCode == HttpStatusCode.NoContent ? first : second;
        var loser = winner == first ? second : first;
        foreach (var response in responses) response.Dispose();
        var completions = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Staff.PostAsync(Receipts + "/" + winner.ReceiptCode + "/complete", null)));
        Assert.Single(completions, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(completions, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in completions) response.Dispose();
        await Expect(await f.Manager.PostAsJsonAsync(Receipts + "/" + loser.ReceiptCode + "/approve", new { }),
            HttpStatusCode.Conflict, "DEVICE_ALREADY_RECEIVED");
        await f.InDb(async db => Assert.Single(await db.Devices.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Receipt_validates_selection_metadata_and_cannot_accept_quantity()
    {
        await using var f = await Fixture.CreateAsync();
        var device = await f.CreateDevice();
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, f.Request(new DeviceReceiptItemRequest { ExistingDeviceCode = "UNKNOWN" }), Json), HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, f.Request(new DeviceReceiptItemRequest { Name = "No category" }), Json), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, f.Request(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode, Name = "Overwrite" }), Json), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, f.Request(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode }, new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode }), Json),
            HttpStatusCode.Conflict, "DEVICE_CODE_CONFLICT");
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, new
        {
            supplierCode = "SUP_QTY", deliveryDate = "2026-10-21", deliveryBatch = "1", requestingDepartmentCode = "PB_TEST",
            items = new[] { new { existingDeviceCode = device.DeviceCode, quantity = 2 } }
        }), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PutAsync(Devices + "/" + device.DeviceCode + "/lock", null), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Receipts, f.Request(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode }), Json), HttpStatusCode.Conflict, "DEVICE_LOCKED");
    }

    [PostgreSqlFact]
    public async Task Completion_audit_failure_rolls_back_quantity_new_devices_and_holds()
    {
        await using var f = await Fixture.CreateAsync();
        var device = await f.CreateDevice();
        var receipt = await f.CreateReceipt(new DeviceReceiptItemRequest { ExistingDeviceCode = device.DeviceCode }, new DeviceReceiptItemRequest { Name = "New", CategoryCode = "CAT_QTY" });
        await f.Approve(receipt.ReceiptCode);
        // Fail the final save after quantity changes, item links, new devices and hold releases.
        await f.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_receipt_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action_code = 'RECEIPT_COMPLETED' THEN RAISE EXCEPTION 'Test audit failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_receipt_audit BEFORE INSERT ON device_receipt_history
            FOR EACH ROW EXECUTE FUNCTION reject_receipt_audit();
            """));
        await Expect(await f.Staff.PostAsync(Receipts + "/" + receipt.ReceiptCode + "/complete", null), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db =>
        {
            Assert.Equal(0, (await db.Devices.SingleAsync()).Quantity);
            Assert.Equal(2, await db.DeviceOperationHolds.CountAsync());
            Assert.All(await db.DeviceReceiptItems.ToListAsync(), x => Assert.Null(x.DeviceId));
            Assert.Empty(await db.DeviceHistories.Where(x => x.ActionCode.StartsWith("DEVICE_RECEIVED_")).ToListAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_receipt_audit ON device_receipt_history; DROP FUNCTION reject_receipt_audit();");
        });
        await f.Complete(receipt.ReceiptCode);
        Assert.Equal(1, (await f.GetDevice(device.DeviceCode)).Quantity);
    }

    [PostgreSqlFact]
    public async Task Migration_backfills_completed_receipts_and_database_rejects_non_binary_quantities()
    {
        await using var f = await Fixture.CreateAsync();
        var pending = await f.CreateDevice();
        var receipt = await f.CreateReceipt(new DeviceReceiptItemRequest { Name = "Received", CategoryCode = "CAT_QTY" });
        await f.Approve(receipt.ReceiptCode);
        await f.Complete(receipt.ReceiptCode);
        await f.InDb(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261101170558_ServiceOrdersAndExports");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(0, (await f.GetDevice(pending.DeviceCode)).Quantity);
        Assert.Equal(1, (await f.GetDevice(Assert.Single(receipt.Items!).DeviceCode)).Quantity);
        await f.InDb(async db =>
        {
            foreach (var quantity in new[] { -1, 2 })
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE devices SET quantity = {quantity}"));
                Assert.Equal("ck_devices_quantity", exception.ConstraintName);
            }
        });
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
            if (code is not null) Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        }
    }

    private sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host, HttpClient staff, HttpClient manager) : IAsyncDisposable
    {
        public HttpClient Staff { get; } = staff;
        public HttpClient Manager { get; } = manager;
        public static async Task<Fixture> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var host = new AccountApiTestHost(database.ConnectionString);
            try
            {
                var seed = await host.SeedAsync();
                var staff = host.CreateClient();
                var manager = host.CreateClient();
                staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.StaffToken);
                manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.ManagerToken);
                var fixture = new Fixture(database, host, staff, manager);
                await fixture.InDb(async db =>
                {
                    db.Add(new DeviceCategory { DisplayCode = "CAT_QTY", Name = "Quantity test" });
                    db.Add(new Supplier { DisplayCode = "SUP_QTY", Name = "Supplier" });
                    await db.SaveChangesAsync();
                });
                return fixture;
            }
            catch { await host.DisposeAsync(); await database.DisposeAsync(); throw; }
        }
        public async Task<DeviceDto> CreateDevice()
        {
            using var response = await Staff.PostAsJsonAsync(Devices, new CreateDeviceRequest
                { Name = "Declared device", CategoryCode = "CAT_QTY", DepartmentCode = "PB_TEST", SerialNumber = "SN-QTY" }, Json);
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<DeviceDto>(Json))!;
        }
        public CreateDeviceReceiptRequest Request(params DeviceReceiptItemRequest[] items) => new()
        {
            SupplierCode = "SUP_QTY", DeliveryDate = new DateOnly(2026, 10, 21), DeliveryBatch = "1",
            RequestingDepartmentCode = "PB_TEST", Items = items.ToList()
        };
        public async Task<DeviceReceiptDto> CreateReceipt(params DeviceReceiptItemRequest[] items)
        {
            using var response = await Staff.PostAsJsonAsync(Receipts, Request(items), Json);
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<DeviceReceiptDto>(Json))!;
        }
        public async Task<DeviceDto> GetDevice(string code) => (await Staff.GetFromJsonAsync<DeviceDto>(Devices + "/" + code, Json))!;
        public Task Approve(string code) => ExpectResponse(Manager.PostAsJsonAsync(Receipts + "/" + code + "/approve", new { }));
        public Task Complete(string code) => ExpectResponse(Staff.PostAsync(Receipts + "/" + code + "/complete", null));
        private static async Task ExpectResponse(Task<HttpResponseMessage> response) => await Expect(await response, HttpStatusCode.NoContent);
        public async Task InDb(Func<CsvcDbContext, Task> action)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>());
        }
        public async ValueTask DisposeAsync()
        {
            Staff.Dispose(); Manager.Dispose(); await host.DisposeAsync(); await database.DisposeAsync();
        }
    }
}
