using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CSVC.UseCases.DeviceLoans.DTOs;
using CSVC.UseCases.DeviceReceipts.DTOs;
using CSVC.UseCases.Devices.DTOs;

namespace CSVC.API.Tests;

public sealed class CatalogReferenceContractTests
{
    public static IEnumerable<object[]> CatalogReferences()
    {
        yield return [typeof(CreateDeviceRequest), nameof(CreateDeviceRequest.CategoryCode), "DM"];
        yield return [typeof(CreateDeviceRequest), nameof(CreateDeviceRequest.ManufacturerCode), "HSX"];
        yield return [typeof(UpdateDeviceRequest), nameof(UpdateDeviceRequest.ManufacturerCode), "HSX"];
        yield return [typeof(DeviceListQuery), nameof(DeviceListQuery.CategoryCode), "DM"];
        yield return [typeof(CreateDeviceReceiptRequest), nameof(CreateDeviceReceiptRequest.SupplierCode), "NCC"];
        yield return [typeof(UpdateDeviceReceiptRequest), nameof(UpdateDeviceReceiptRequest.SupplierCode), "NCC"];
        yield return [typeof(DeviceReceiptItemRequest), nameof(DeviceReceiptItemRequest.CategoryCode), "DM"];
        yield return [typeof(DeviceReceiptListQuery), nameof(DeviceReceiptListQuery.SupplierCode), "NCC"];
        yield return [typeof(LoanDeviceOptionsQuery), nameof(LoanDeviceOptionsQuery.CategoryCode), "DM"];
    }

    [Theory]
    [MemberData(nameof(CatalogReferences))]
    public void References_accept_generated_and_manual_catalog_codes_up_to_64_characters(Type type, string field, string prefix)
    {
        var property = type.GetProperty(field)!;
        var model = Activator.CreateInstance(type)!;
        foreach (var code in new[] { prefix + Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('A', 64) })
        {
            property.SetValue(model, code);
            var errors = new List<ValidationResult>();
            Assert.True(Validator.TryValidateProperty(code, new ValidationContext(model) { MemberName = field }, errors),
                string.Join("; ", errors.Select(x => x.ErrorMessage)));
        }
        var tooLong = new string('A', 65);
        property.SetValue(model, tooLong);
        Assert.False(Validator.TryValidateProperty(tooLong, new ValidationContext(model) { MemberName = field }, []));
    }

    [Fact]
    public void Receipt_with_ten_new_devices_accepts_the_catalog_code_returned_by_category_creation()
    {
        var category = "DM" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var supplier = "NCC" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var json = JsonSerializer.Serialize(new
        {
            supplierCode = supplier, requestingDepartmentCode = "PB_QTTB", deliveryDate = "2026-11-06",
            deliveryBatch = "Dot 1", notes = (string?)null,
            items = Enumerable.Range(1, 10).Select(i => new
            {
                categoryCode = category, name = $"May in {i}", deviceCode = (string?)null,
                serialNumber = $"SN-{i}", parentDeviceCode = (string?)null
            })
        });
        var receipt = JsonSerializer.Deserialize<CreateDeviceReceiptRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        AssertValid(receipt);
        Assert.Equal(10, receipt.Items.Count);
        foreach (var item in receipt.Items) { AssertValid(item); Assert.Equal(category, item.CategoryCode); }
        Assert.Equal(supplier, receipt.SupplierCode);
    }

    [Theory]
    [InlineData(typeof(CreateDeviceRequest), nameof(CreateDeviceRequest.DeviceCode))]
    [InlineData(typeof(CreateDeviceRequest), nameof(CreateDeviceRequest.DepartmentCode))]
    [InlineData(typeof(CreateDeviceRequest), nameof(CreateDeviceRequest.ResponsibleAccountCode))]
    [InlineData(typeof(DeviceReceiptItemRequest), nameof(DeviceReceiptItemRequest.ExistingDeviceCode))]
    [InlineData(typeof(DeviceReceiptItemRequest), nameof(DeviceReceiptItemRequest.ParentDeviceCode))]
    public void Non_catalog_identifiers_retain_their_32_character_limit(Type type, string field)
    {
        var model = Activator.CreateInstance(type)!;
        var property = type.GetProperty(field)!;
        property.SetValue(model, new string('A', 32));
        Assert.True(Validator.TryValidateProperty(property.GetValue(model), new ValidationContext(model) { MemberName = field }, []));
        property.SetValue(model, new string('A', 33));
        Assert.False(Validator.TryValidateProperty(property.GetValue(model), new ValidationContext(model) { MemberName = field }, []));
    }

    [Fact]
    public async Task Swagger_exposes_the_same_catalog_reference_limits_as_model_validation()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var (model, field) in new[]
        {
            ("CreateDeviceRequest", "categoryCode"), ("CreateDeviceRequest", "manufacturerCode"),
            ("UpdateDeviceRequest", "manufacturerCode"), ("CreateDeviceReceiptRequest", "supplierCode"),
            ("UpdateDeviceReceiptRequest", "supplierCode"), ("DeviceReceiptItemRequest", "categoryCode")
        })
            Assert.Equal(64, schemas.GetProperty(model).GetProperty("properties").GetProperty(field).GetProperty("maxLength").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Full_frontend_receipt_and_device_payloads_round_trip_long_catalog_codes()
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var staff = factory.CreateClient();
        using var manager = factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.StaffToken);
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.ManagerToken);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var settings = await manager.GetFromJsonAsync<JsonElement>("/api/v1/settings/equipment-department");
        using var configure = await manager.PutAsJsonAsync("/api/v1/settings/equipment-department", new
        { departmentCode = "PB_TEST", version = settings.GetProperty("version").GetInt32() });
        Assert.True(configure.StatusCode == HttpStatusCode.NoContent, await configure.Content.ReadAsStringAsync());
        foreach (var length in new[] { 35, 64 })
        {
            var category = "DM" + new string('A', length - 2);
            var manufacturer = "HSX" + new string('B', length - 3);
            var supplier = "NCC" + new string('C', length - 3);
            foreach (var (resource, code) in new[] { ("device-categories", category), ("manufacturers", manufacturer), ("suppliers", supplier) })
            {
                using var created = await manager.PostAsJsonAsync($"/api/v1/{resource}", new { displayCode = code, name = resource + length });
                Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            }
            using var response = await staff.PostAsJsonAsync("/api/v1/devices", new
            {
                name = "Registered printer", categoryCode = category, manufacturerCode = manufacturer,
                departmentCode = "PB_TEST", condition = "Good", serialNumber = "SN-" + length,
                purchasePrice = 123456.50m, notes = (string?)null
            });
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var device = (await response.Content.ReadFromJsonAsync<DeviceDto>(json))!;
            Assert.Equal(category, device.CategoryCode);
            Assert.Equal(manufacturer, device.ManufacturerCode);
            using var update = await staff.PutAsJsonAsync($"/api/v1/devices/{device.DeviceCode}", new
            { name = "Updated printer", manufacturerCode = manufacturer, version = device.Version });
            Assert.True(update.StatusCode == HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync());
            using var devices = await staff.GetAsync($"/api/v1/devices?categoryCode={category}");
            Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
            using var eligible = await staff.GetAsync($"/api/v1/device-loans/eligible-devices?categoryCode={category}");
            Assert.True(eligible.StatusCode == HttpStatusCode.OK, await eligible.Content.ReadAsStringAsync());
            using var receiptResponse = await staff.PostAsJsonAsync("/api/v1/device-receipts", new
            {
                supplierCode = supplier, requestingDepartmentCode = "PB_TEST", deliveryDate = "2026-11-06", deliveryBatch = "Batch-" + length,
                items = Enumerable.Range(1, 10).Select(i => new { categoryCode = category, name = $"Printer {i}", deviceCode = (string?)null, serialNumber = $"SN-{length}-{i}", parentDeviceCode = (string?)null })
            });
            Assert.True(receiptResponse.StatusCode == HttpStatusCode.Created, await receiptResponse.Content.ReadAsStringAsync());
            var receipt = (await receiptResponse.Content.ReadFromJsonAsync<DeviceReceiptDto>(json))!;
            Assert.Equal(supplier, receipt.SupplierCode);
            Assert.Equal(10, receipt.Items!.Count);
            Assert.All(receipt.Items, item => Assert.Equal(category, item.CategoryCode));
            using var list = await staff.GetAsync($"/api/v1/device-receipts?supplierCode={supplier}");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var approve = await manager.PostAsJsonAsync($"/api/v1/device-receipts/{receipt.ReceiptCode}/approve", new { });
            Assert.True(approve.StatusCode == HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());
            using var complete = await staff.PostAsync($"/api/v1/device-receipts/{receipt.ReceiptCode}/complete", null);
            Assert.True(complete.StatusCode == HttpStatusCode.NoContent, await complete.Content.ReadAsStringAsync());
            foreach (var item in receipt.Items)
            {
                var received = (await staff.GetFromJsonAsync<DeviceDto>($"/api/v1/devices/{item.DeviceCode}", json))!;
                Assert.Equal(category, received.CategoryCode);
                Assert.Equal(1, received.Quantity);
            }
        }
    }

    private static void AssertValid(object model)
    {
        var errors = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true),
            string.Join("; ", errors.Select(x => x.ErrorMessage)));
    }
}
