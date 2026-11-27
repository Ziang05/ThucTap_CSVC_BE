using System.Net;
using System.Net.Http.Json;
using CSVC.Core.ProcurementRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.API.Tests;

public sealed class PurchaseLifecycleMigrationTests
{
    [PostgreSqlFact]
    public async Task Existing_purchase_rows_are_backfilled_from_actual_documents_without_changing_repairs_or_device_custody()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(stockInStorage: true);
        const string root = "/api/v1/procurement-requests/";
        async Task<string> Request(int quantity = 1, bool repair = false)
        {
            var code = await SourceRequestAssignmentTests.Create(f, repair: repair, quantity: quantity);
            await SourceRequestAssignmentTests.Assign(f, code); return code;
        }
        async Task Allocate(string code, string device)
            => await SourceRequestAssignmentTests.Read(await f.Staff.PostAsJsonAsync("/api/v1/device-transfers", new {
                sourceRequestCode = code, sourceDepartmentCode = "PB_TEST", targetDepartmentCode = "PB_TARGET",
                targetResponsibleAccountCode = "TK_DEPARTMENT", reason = "Allocation", items = new[] { new { sourceLineCode = "L1", deviceCode = device } }
            }), HttpStatusCode.Created);
        var allocated = await Request(); await Allocate(allocated, "TB-ROLE-001");
        var delivered = await Request(); await Allocate(delivered, "TB-ROLE-002");
        using (var received = await f.Department.PostAsJsonAsync(root + delivered + "/receive", new {
            requestId = Guid.NewGuid(), version = await SourceRequestAssignmentTests.Version(f, delivered), receivedInFull = true,
            items = new[] { new { deviceCode = "TB-ROLE-002", condition = "Good" } }
        })) Assert.True(received.IsSuccessStatusCode, await received.Content.ReadAsStringAsync());
        var stocked = await Request(quantity: 3); await PurchaseTrackingTests.Track(f, stocked);
        await SourceRequestAssignmentTests.Read(await f.Manager.PostAsJsonAsync("/api/v1/suppliers", new { displayCode = "MIGRATION_SUPPLIER", name = "Supplier" }), HttpStatusCode.Created);
        var receipt = await SourceRequestAssignmentTests.Read(await f.Staff.PostAsJsonAsync("/api/v1/device-receipts", new {
            sourceRequestCode = stocked, supplierCode = "MIGRATION_SUPPLIER", requestingDepartmentCode = "PB_TARGET", deliveryDate = "2026-10-21", deliveryBatch = "1",
            items = new[] { new { sourceLineCode = "L1", deviceCode = "MIGRATION-NEW", categoryCode = "CAT_ROLE", name = "Delivered equipment" } }
        }), HttpStatusCode.Created);
        var receiptRoute = "/api/v1/device-receipts/" + receipt.GetProperty("receiptCode").GetString();
        using (var approved = await f.Manager.PostAsync(receiptRoute + "/approve", null)) Assert.True(approved.IsSuccessStatusCode);
        using (var completed = await f.Staff.PostAsync(receiptRoute + "/complete", null)) Assert.True(completed.IsSuccessStatusCode);
        var initial = await Request(); var repairCode = await Request(repair: true);
        await f.InDb(async db => {
            foreach (var request in await db.Set<ProcurementRequest>().ToListAsync())
            {
                request.Status = request.DisplayCode == stocked ? ProcurementRequestStatus.TrackingPurchase : ProcurementRequestStatus.InProgress;
                request.ClosedAt = null; request.Result = null; request.Version++;
            }
            await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync("20261127010000_CompleteMaterialIssuesOnPosting");
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            var requests = await db.Set<ProcurementRequest>().ToDictionaryAsync(request => request.DisplayCode);
            Assert.Equal(ProcurementRequestStatus.AwaitingReceipt, requests[allocated].Status);
            Assert.Equal(ProcurementRequestStatus.AwaitingHandover, requests[stocked].Status);
            Assert.Equal(ProcurementRequestStatus.Completed, requests[delivered].Status);
            Assert.Equal("Success", requests[delivered].Result); Assert.NotNull(requests[delivered].ClosedAt);
            Assert.Equal(ProcurementRequestStatus.Received, requests[initial].Status);
            Assert.Equal(ProcurementRequestStatus.InProgress, requests[repairCode].Status);
            var owner = (await db.Departments.SingleAsync(department => department.Code == "PB_TEST")).Id;
            Assert.Equal(owner, (await db.Devices.SingleAsync(device => device.DeviceCode == "TB-ROLE-001")).DepartmentId);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
