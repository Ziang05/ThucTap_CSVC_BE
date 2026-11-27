using CSVC.Core.Accounts;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.DeviceTransfers;
using CSVC.UseCases.DeviceTransfers.DTOs;

namespace CSVC.UseCases.ProcurementRequests;

public sealed partial class ProcurementRequestService
{
    public async Task<ProcurementStock> StockAsync(string code, CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct);
        if (actor.Role == RoleCodes.DepartmentStaff) throw ForbiddenSource();
        var request = await requests.GetAsync(code, false, ct) ?? throw Missing();
        RequireVisible(request, actor);
        RequireEquipmentPurchase(request);
        return await StockAsync(request, ct);
    }

    private static void RequireEquipmentPurchase(ProcurementRequest request)
    {
        if (request.RequestType != ProcurementRequestType.Purchase || !request.Lines.Any(l => l.Kind == ProcurementLineKind.Equipment))
            throw Invalid("Select a purchasing request with equipment lines.");
    }

    private async Task<ProcurementStock> StockAsync(ProcurementRequest request, CancellationToken ct)
    {
        var departmentId = await equipment.RequireConfiguredAsync(ct);
        var remaining = new Dictionary<string, int>(await uow.Devices.AvailableStockByCategoryAsync(departmentId, ct), StringComparer.OrdinalIgnoreCase);
        var result = new List<ProcurementStockLine>();
        // Allocate specific categories before unrestricted lines; the same device cannot satisfy two source lines.
        foreach (var line in request.Lines.Where(l => l.Kind == ProcurementLineKind.Equipment)
            .OrderBy(l => l.CategoryCode is null).ThenBy(l => l.SourceLineCode, StringComparer.Ordinal))
        {
            var required = checked((int)line.Quantity);
            var available = line.CategoryCode is { } category ? remaining.GetValueOrDefault(category) : remaining.Values.Sum();
            var used = Math.Min(required, available);
            if (line.CategoryCode is { } code) remaining[code] = available - used;
            else
            {
                var needed = used;
                foreach (var key in remaining.Keys.Order(StringComparer.Ordinal).ToArray())
                {
                    var take = Math.Min(needed, remaining[key]);
                    remaining[key] -= take; needed -= take;
                    if (needed == 0) break;
                }
            }
            result.Add(new(line.SourceLineCode, line.Description, line.CategoryCode, required, used, required - used));
        }
        return new(result.All(line => line.MissingQuantity == 0), result.OrderBy(line => line.SourceLineCode, StringComparer.Ordinal).ToArray());
    }

    public Task TrackPurchaseAsync(string code, TrackPurchaseRequest input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ReadActorAsync(token); Validate(input);
            var request = await requests.GetAsync(code, true, token) ?? throw Missing();
            RequireVisible(request, actor);
            var hash = CommandHash(request.DisplayCode, input);
            if (await ReplayCommandAsync(request, input.RequestId, actor.Id, hash, token)) return true;
            await EditableAsync(code, input.Version, token);
            await assignment.RequireAsync(code, actor.Id, token);
            RequireEquipmentPurchase(request);
            if (request.RequiresReview || request.MappingIssue is not null)
                throw Conflict("SOURCE_REQUEST_NOT_READY", "Resolve source references or changes before tracking purchases.");
            if (request.Status == ProcurementRequestStatus.TrackingPurchase)
                throw Conflict("PURCHASE_ALREADY_TRACKED", "This request is already being tracked.");
            if ((await workflow.DocumentsAsync(request, token)).Any(document => document.Open || document.Executed))
                throw Conflict("SOURCE_DOCUMENTS_EXIST", "Handle the existing documents before choosing purchase tracking.");
            var stock = await StockAsync(request, token);
            if (stock.CanAllocate)
                throw Conflict("SOURCE_STOCK_SUFFICIENT", "The warehouse can fulfill the entire request. Allocate the available equipment.");
            request.Status = ProcurementRequestStatus.TrackingPurchase; request.Version++;
            await CommandEventAsync(request, input.RequestId, actor.Id, hash, "PURCHASE_TRACKING_STARTED", "TrackingPurchase", stock, token);
            return true;
        }, ct);

    public Task ReceivePurchaseAsync(string code, ReceivePurchaseRequest input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ReadActorAsync(token); Validate(input);
            var request = await requests.GetAsync(code, true, token) ?? throw Missing();
            RequireVisible(request, actor);
            if (!IsReceiver(request, actor)) throw ForbiddenSource();
            var hash = CommandHash(request.DisplayCode, input);
            if (await ReplayCommandAsync(request, input.RequestId, actor.Id, hash, token)) return true;
            await EditableAsync(code, input.Version, token);
            RequireEquipmentPurchase(request);
            if (!input.ReceivedInFull) throw Invalid("Confirm that all equipment has physically arrived at the requesting department.");
            if (request.Status != ProcurementRequestStatus.AwaitingReceipt || request.RequiresReview || request.MappingIssue is not null)
                throw Conflict("SOURCE_REQUEST_NOT_READY", "Only a request awaiting physical receipt can be confirmed.");
            if (request.Lines.Any(line => line.Kind == ProcurementLineKind.Material))
                throw Conflict("COMBINED_DELIVERY_REQUIRED", "Receive equipment and materials together from the material issue.");
            var allocation = (await workflow.DocumentsAsync(request, token)).SingleOrDefault(document => document.Kind == RequestDocumentKind.Allocation && document.Open)
                ?? throw Conflict("SOURCE_ALLOCATION_REQUIRED", "An active allocation is required before confirming physical receipt.");
            var transfer = (await uow.DeviceTransfers.FirstOrDefaultAsync(document => document.TransferCode == allocation.DocumentCode, token))!;
            // Earlier allocations may still have the separate intent step. Complete both under this physical-receipt transaction.
            if (transfer.Status == DeviceTransferStatus.PendingConfirmation)
                await purchaseDelivery.ConfirmReceiptIntentAsync(transfer.TransferCode, true, new TransferDecisionRequest(), token);
            await purchaseDelivery.HandoverCompleteAsync(transfer.TransferCode, new TransferHandoverRequest { Items = input.Items }, token);
            await CommandEventAsync(request, input.RequestId, actor.Id, hash, "PHYSICAL_RECEIPT_CONFIRMED", request.Status.ToString(),
                new { allocation.DocumentCode, input.ReceivedInFull, input.Items }, token);
            return true;
        }, ct);
}
