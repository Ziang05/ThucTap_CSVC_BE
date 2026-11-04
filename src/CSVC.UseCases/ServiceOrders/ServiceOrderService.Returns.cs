using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.ServiceOrders;

public sealed partial class ServiceOrderService
{
    public Task<ExportReturnDto> CreateReturnAsync(CreateExportReturnRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token); Validate(request); RequireRequest(request.RequestId);
            if (request.Items is null || request.Items.Any(i => i is null)) throw Validation("Return items are required.");
            ValidateSelection(request.Items.Select(i => i.ItemNo));
            foreach (var input in request.Items)
            {
                Validate(input);
                if (input.ConditionAtReturn is not (DeviceCondition.Good or DeviceCondition.Damaged)) throw Validation("Physical return condition must be Good or Damaged.");
            }
            if (Clean(request.HandoverPersonName) is null || request.ReceivedAt == default) throw Validation("Handover person and actual receivedAt are required.");
            var hash = Hash(new { source = Code(request.ExportSlipCode), handover = Clean(request.HandoverPersonName),
                contact = Clean(request.HandoverPersonContact), receiver = Code(request.ReceivedByAccountCode), receivedAt = request.ReceivedAt.ToUniversalTime(),
                notes = Clean(request.Notes), items = request.Items.OrderBy(i => i.ItemNo).Select(i => new { i.ItemNo, i.ConditionAtReturn, note = Clean(i.ReturnNote) }) });
            var prior = await orders.GetReturnByRequestAsync(request.RequestId, token);
            if (prior is not null)
            {
                if (prior.CreatedByAccountId != actor || prior.CreateRequestHash != hash) throw IdempotencyConflict();
                return await ToReturnDtoAsync(prior, token);
            }
            var now = clock.GetUtcNow();
            if (request.ReceivedAt > now) throw Validation("receivedAt cannot be in the future.");
            var order = await LoadExportAsync(request.ExportSlipCode, true, token); var export = Export(order, request.ExportSlipCode);
            if (export.Status is not (ServiceWorkflowStatus.InProgress or ServiceWorkflowStatus.Completed)) throw InvalidTransition();
            var receiver = await unitOfWork.Accounts.FirstOrDefaultAsync(a => a.AccountCode == Code(request.ReceivedByAccountCode) && !a.IsLock, token) ?? throw Validation("Receiver must be an active equipment account.");
            var role = await unitOfWork.Roles.GetByIdAsync(receiver.RoleId, token);
            if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff)) throw Validation("Receiver must belong to equipment management.");
            var profile = await unitOfWork.UserProfiles.GetByIdAsync(receiver.UserProfileId, token) ?? throw Validation("Receiver profile is unavailable.");
            await equipment.RequireMemberAsync(role.Code, profile.DepartmentId, token);
            var selected = SelectItems(order, request.Items.Select(i => i.ItemNo));
            if (selected.Any(i => !export.Items.Any(l => l.ServiceOrderItemId == i.Id))) throw Validation("Return items must belong to the selected export.");
            await transaction.LockDevicesAsync(selected.Select(i => i.DeviceId), token);
            var slip = new ExportReturnSlip { ExportSlipId = export.Id, SequenceNo = await orders.NextReturnSequenceAsync(export.Id, token),
                DisplayCode = BusinessCode.NewRandom("NL"), FacilityNameSnapshot = export.FacilityNameSnapshot,
                HandoverPersonName = Clean(request.HandoverPersonName)!, HandoverPersonContact = Clean(request.HandoverPersonContact),
                ReceivedByAccountId = receiver.Id, ReceivedAt = request.ReceivedAt.ToUniversalTime(), CreatedByAccountId = actor, CreatedAt = now,
                CreateRequestId = request.RequestId, CreateRequestHash = hash, Notes = Clean(request.Notes) };
            foreach (var item in selected)
            {
                var line = export.Items.Single(l => l.ServiceOrderItemId == item.Id);
                if (line.Result != ExportResult.Success || await returnItems.ExistsAsync(i => i.ExportSlipItemId == line.Id, token))
                    throw Conflict("EXPORT_ITEM_NOT_RETURNABLE", "Select successful export items that have not been returned.");
                var guard = await OwnGuardAsync(item, line, ExportGuardState.AwaitingReturn, token);
                var input = request.Items.Single(i => i.ItemNo == item.ItemNo);
                var returned = new ExportReturnSlipItem { ReturnSlipId = slip.Id, ExportSlipId = export.Id, ExportSlipItemId = line.Id,
                    DeviceCodeSnapshot = line.DeviceCodeSnapshot, DeviceNameSnapshot = line.DeviceNameSnapshot, SerialSnapshot = line.SerialSnapshot,
                    ConditionAtReturn = input.ConditionAtReturn, ReturnNote = Clean(input.ReturnNote) };
                slip.Items.Add(returned); await guards.DeleteAsync(guard, token);
                await AuditReturnAsync(slip, returned, actor, "RETURN_ITEM_RECEIVED", token);
            }
            await returns.AddAsync(slip, token);
            await AuditReturnAsync(slip, null, actor, "RETURN_SLIP_RECORDED", token);
            return await ToReturnDtoAsync(slip, token);
        }, ct);

    public async Task<ExportReturnDto> GetReturnAsync(string code, CancellationToken ct)
    {
        await RequireActorAsync(null, ct);
        return await ToReturnDtoAsync(await orders.GetReturnAsync(Code(code), ct) ?? throw Missing("EXPORT_RETURN_NOT_FOUND"), ct);
    }
    public async Task<PagedResult<ExportReturnDto>> ListReturnsAsync(ExportReturnListQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct); Validate(query);
        var page = await orders.ListReturnsAsync(query, ct);
        var parents = await orders.GetByExportIdsAsync(page.Items.Select(r => r.ExportSlipId).Distinct().ToArray(), ct);
        var codes = await orders.AccountCodesAsync(page.Items.SelectMany(r => new[] { r.CreatedByAccountId, r.ReceivedByAccountId }).Distinct(), ct);
        var result = page.Items.Select(r => ToReturnDto(r, parents.Single(o => o.ExportSlips.Any(e => e.Id == r.ExportSlipId)), codes)).ToArray();
        return new(result, page.TotalCount, page.Page, page.PageSize);
    }
    public async Task<PagedResult<ServiceHistoryDto>> ReturnHistoryAsync(string code, ServiceHistoryQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct); Validate(query);
        var slip = await orders.GetReturnAsync(Code(code), ct) ?? throw Missing("EXPORT_RETURN_NOT_FOUND");
        return await orders.ReturnHistoryAsync(slip.Id, query, ct);
    }
    private Task AuditReturnAsync(ExportReturnSlip slip, ExportReturnSlipItem? item, Guid actor, string action, CancellationToken ct)
        => returnHistory.AddAsync(new ExportReturnHistory { ReturnSlipId = slip.Id, ReturnSlipItemId = item?.Id, RequestId = slip.CreateRequestId,
            RequestPayloadHash = slip.CreateRequestHash, TargetKey = item is null ? "header" : "item:" + item.Id,
            ActorAccountId = actor, ActionCode = action, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            AfterData = JsonSerializer.Serialize(new { returnCode = slip.DisplayCode, deviceCode = item?.DeviceCodeSnapshot,
                conditionAtReturn = item?.ConditionAtReturn.ToString(), receivedAt = slip.ReceivedAt, createdAt = slip.CreatedAt }) }, ct);
    private async Task<ExportReturnDto> ToReturnDtoAsync(ExportReturnSlip slip, CancellationToken ct)
    {
        var order = (await orders.GetByExportIdsAsync([slip.ExportSlipId], ct)).SingleOrDefault() ?? throw Missing("EXPORT_SLIP_NOT_FOUND");
        var codes = await orders.AccountCodesAsync([slip.CreatedByAccountId, slip.ReceivedByAccountId], ct);
        return ToReturnDto(slip, order, codes);
    }
    private static ExportReturnDto ToReturnDto(ExportReturnSlip slip, ServiceOrder order, IReadOnlyDictionary<Guid,string> codes)
    {
        var export = order.ExportSlips.Single(e => e.Id == slip.ExportSlipId);
        return new(slip.DisplayCode, export.DisplayCode, slip.SequenceNo, slip.Status, slip.FacilityNameSnapshot,
            slip.HandoverPersonName, slip.HandoverPersonContact, ActorCode(codes, slip.ReceivedByAccountId)!, slip.ReceivedAt,
            ActorCode(codes, slip.CreatedByAccountId)!, slip.CreatedAt, slip.Notes, slip.Version,
            slip.Items.Select(i => new ExportReturnItemDto(order.Items.Single(si => si.Id == export.Items.Single(ei => ei.Id == i.ExportSlipItemId).ServiceOrderItemId).ItemNo,
                i.DeviceCodeSnapshot, i.DeviceNameSnapshot, i.SerialSnapshot, i.ConditionAtReturn, i.ReturnNote)).OrderBy(i => i.ItemNo).ToArray());
    }
}
