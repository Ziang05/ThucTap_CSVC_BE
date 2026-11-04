using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.DeviceLoans;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure.Persistence;
using CSVC.Infrastructure.Persistence.Repositories;
using CSVC.UseCases.Devices.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class DeviceTimelineTests
{
    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Device_and_global_timelines_translate_to_postgres_for_empty_and_mixed_history(bool withHistory)
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var host = new AccountApiTestHost(database.ConnectionString);
        var seed = await host.SeedAsync();
        var now = host.Clock.GetUtcNow();
        Guid deviceId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var category = new DeviceCategory { DisplayCode = "CAT_TIMELINE", Name = "Timeline category" };
            var device = new Device { DeviceCode = "TB-TIMELINE", Barcode = "BC" + new string('A', 30), Name = "Timeline device", DepartmentId = seed.Department, CategoryId = category.Id, Quantity = 1 };
            deviceId = device.Id;
            db.AddRange(category, device, new DeviceIdentity { DeviceCode = device.DeviceCode, Barcode = device.Barcode, ReservedAt = now });
            if (withHistory)
            {
                var supplier = new Supplier { DisplayCode = "SUP_TIMELINE", Name = "Timeline supplier" };
                var receipt = new DeviceReceipt
                {
                    ReceiptCode = "PN-TIMELINE", SupplierId = supplier.Id, RequestingDepartmentId = seed.Department,
                    CreatorAccountId = seed.Staff, CreatedAt = now.AddDays(-1), DeliveryDate = DateOnly.FromDateTime(now.UtcDateTime), DeliveryBatch = "1",
                    Status = DeviceReceiptStatus.Completed, CompletedAt = now
                };
                receipt.Items.Add(new DeviceReceiptItem { Name = device.Name, DeviceCode = device.DeviceCode, Barcode = device.Barcode, CategoryId = category.Id, DeviceId = device.Id });
                var loan = new DeviceLoan
                {
                    WorkflowVersion = 1, LoanCode = "MT-TIMELINE", BorrowerAccountId = seed.Staff, BorrowerDepartmentId = seed.Department,
                    Purpose = "Timeline loan", DueAt = now.AddDays(7), CreatorAccountId = seed.Staff, CreatedAt = now
                };
                loan.Items.Add(new DeviceLoanItem { DeviceId = device.Id });
                var target = new Department { Code = "PB_TIMELINE", Name = "Timeline target" };
                var transfer = new DeviceTransfer
                {
                    TransferCode = "DC-TIMELINE", SourceDepartmentId = target.Id, TargetDepartmentId = seed.Department,
                    TargetResponsibleAccountId = seed.Staff, Reason = "Timeline transfer", CreatorAccountId = seed.Staff, CreatedAt = now,
                    Status = DeviceTransferStatus.Completed, CompletedAt = now
                };
                transfer.Items.Add(new DeviceTransferItem { DeviceId = device.Id });
                db.AddRange(supplier, receipt, loan, target, transfer);
                db.Add(new DeviceHistory { DeviceId = device.Id, RequestId = Guid.NewGuid(), ActorAccountId = seed.Manager,
                    ActionCode = "DEVICE_CREATED", Outcome = AuditOutcome.Success, OccurredAt = now.AddMinutes(-4), ChangedFields = "[\"name\"]", AfterData = "{\"quantity\":1}" });
                db.Add(new DeviceReceiptHistory { ReceiptId = receipt.Id, RequestId = Guid.NewGuid(),
                    ActionCode = "RECEIPT_COMPLETED", Outcome = AuditOutcome.Success, OccurredAt = now.AddMinutes(-3) });
                db.Add(new DeviceLoanHistory { LoanId = loan.Id, RequestId = Guid.NewGuid(), ActorAccountId = seed.Staff,
                    ActionCode = "LOAN_CREATED", Outcome = AuditOutcome.Success, OccurredAt = now.AddMinutes(-2) });
                db.Add(new DeviceTransferHistory { TransferId = transfer.Id, RequestId = Guid.NewGuid(), ActorAccountId = seed.Staff,
                    ActionCode = "TRANSFER_COMPLETED", Outcome = AuditOutcome.Success, OccurredAt = now.AddMinutes(-1) });
            }
            await db.SaveChangesAsync();
            // Exercise the real provider directly so query-translation failures retain their diagnostic message.
            var page = await new DeviceHistoryRepository(db).ListTimelineForDeviceAsync(deviceId, new DeviceHistoryQuery());
            Assert.Equal(withHistory ? 4 : 0, page.TotalCount);
        }
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.ManagerToken);
        const string detailRoute = "/api/v1/devices/TB-TIMELINE/history";
        var detail = await GetPageAsync(client, detailRoute);
        var global = await GetPageAsync(client, "/api/v1/history/devices?deviceCode=TB-TIMELINE");
        Assert.Equal(withHistory ? 4 : 0, detail.GetProperty("totalCount").GetInt32());
        Assert.Equal(detail.GetRawText(), global.GetRawText());
        if (!withHistory) return;

        var events = detail.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { "TRANSFER", "LOAN", "RECEIPT", "DEVICE" }, events.Select(x => x.GetProperty("eventType").GetString()));
        Assert.Equal(new string?[] { "DC-TIMELINE", "MT-TIMELINE", "PN-TIMELINE", null }, events.Select(x => x.GetProperty("businessCode").GetString()));
        Assert.Equal(JsonValueKind.Null, events[2].GetProperty("actorAccountCode").ValueKind);
        Assert.Equal("TK_MANAGER", events[3].GetProperty("actorAccountCode").GetString());
        using var snapshot = JsonDocument.Parse(events[3].GetProperty("afterData").GetString()!);
        Assert.Equal(1, snapshot.RootElement.GetProperty("quantity").GetInt32());

        var first = await GetPageAsync(client, detailRoute + "?page=1&pageSize=2");
        var second = await GetPageAsync(client, detailRoute + "?page=2&pageSize=2");
        Assert.Equal(4, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(4, second.GetProperty("totalCount").GetInt32());
        var pagedActions = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("actionCode").GetString());
        Assert.Equal(events.Select(x => x.GetProperty("actionCode").GetString()), pagedActions);
        var filtered = await GetPageAsync(client, detailRoute + "?eventType=LOAN&departmentCode=PB_TEST&actorAccountCode=TK_STAFF"
            + "&fromDate=" + Uri.EscapeDataString(now.AddMinutes(-2).ToString("O")) + "&toDate=" + Uri.EscapeDataString(now.ToString("O")));
        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal("MT-TIMELINE", filtered.GetProperty("items")[0].GetProperty("businessCode").GetString());
        var missing = await GetPageAsync(client, "/api/v1/history/devices?deviceCode=TB-UNKNOWN");
        Assert.Equal(0, missing.GetProperty("totalCount").GetInt32());
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
