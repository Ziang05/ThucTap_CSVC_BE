using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.ProcurementRequests;

public sealed partial class ProcurementRequestService
{
    public async Task<IReadOnlyList<RequestAssigneeDto>> AssigneesAsync(CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct);
        if (actor.Role != RoleCodes.EquipmentManager) throw ForbiddenSource();
        var role = (await uow.Roles.FirstOrDefaultAsync(r => r.Code == RoleCodes.EquipmentStaff, ct))!;
        var result = new List<RequestAssigneeDto>();
        foreach (var account in await uow.Accounts.FindAsync(a => a.RoleId == role.Id && !a.IsLock, ct))
        {
            var profile = await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct);
            if (profile is not null && await uow.Departments.ExistsAsync(d => d.Id == profile.DepartmentId && d.Code == actor.DepartmentCode && d.Status == EntityStatus.Active, ct))
                result.Add(new(account.AccountCode, profile.FullName));
        }
        return result.OrderBy(r => r.FullName).ThenBy(r => r.AccountCode).ToArray();
    }
    private async Task<RequestAssignmentDto?> AssignmentAsync(ProcurementRequest request, CancellationToken ct)
    {
        if (request.AssigneeAccountId is not { } id) return null;
        var account = (await uow.Accounts.GetByIdAsync(id, ct))!;
        var profile = (await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct))!;
        var manager = (await uow.Accounts.GetByIdAsync(request.AssignedByAccountId!.Value, ct))!;
        return new(account.AccountCode, profile.FullName, manager.AccountCode, request.AssignedAt!.Value);
    }
    public Task AssignAsync(string code, AssignSourceRequest input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ReadActorAsync(token); Validate(input);
            if (actor.Role != RoleCodes.EquipmentManager) throw ForbiddenSource();
            var request = await requests.GetAsync(code, true, token) ?? throw Missing();
            var hash = CommandHash(request.DisplayCode, input);
            if (await ReplayCommandAsync(request, input.RequestId, actor.Id, hash, token)) return true;
            await EditableAsync(code, input.Version, token);
            var before = await AssignmentAsync(request, token);
            var assigneeCode = BusinessCode.Normalize(input.AssigneeAccountCode);
            Guid? selected = null;
            if (!string.IsNullOrWhiteSpace(assigneeCode))
            {
                var options = await AssigneesAsync(token);
                if (!options.Any(o => o.AccountCode == assigneeCode)) throw Invalid("Select an active employee in the configured QTTB department.");
                selected = (await uow.Accounts.FirstOrDefaultAsync(a => a.AccountCode == assigneeCode, token))!.Id;
            }
            if (before is not null && string.IsNullOrWhiteSpace(input.Reason)) throw Invalid("Provide a reason when changing or removing responsibility.");
            if (selected == request.AssigneeAccountId) throw Invalid("Select a different assignee.");
            request.AssigneeAccountId = selected;
            request.AssignedByAccountId = selected is null ? null : actor.Id;
            request.AssignedAt = selected is null ? null : clock.GetUtcNow(); request.Version++;
            await CommandEventAsync(request, input.RequestId, actor.Id, hash, "ASSIGNMENT_CHANGED", selected is null ? "Unassigned" : "Assigned",
                new { before, after = await AssignmentAsync(request, token), reason = input.Reason }, token);
            return true;
        }, ct);

    public async Task<IReadOnlyList<RequestWorkloadDto>> WorkloadAsync(CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct);
        if (actor.Role != RoleCodes.EquipmentManager) throw ForbiddenSource();
        var all = await records.GetAllAsync(ct);
        var result = new List<RequestWorkloadDto>();
        foreach (var group in all.GroupBy(r => r.AssigneeAccountId))
        {
            var account = group.Key is null ? null : await uow.Accounts.GetByIdAsync(group.Key, ct);
            var name = account is null ? "Chưa phân công" : (await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct))!.FullName;
            result.Add(new(account?.AccountCode, name,
                group.Count(r => r.Status is not (ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled)),
                group.Count(r => r.Status is ProcurementRequestStatus.WaitingForGoods or ProcurementRequestStatus.WaitingForExternal or ProcurementRequestStatus.AwaitingHandover or ProcurementRequestStatus.TrackingPurchase or ProcurementRequestStatus.AwaitingReceipt),
                group.Count(r => r.Status == ProcurementRequestStatus.Completed), group.Count(r => r.Status == ProcurementRequestStatus.Cancelled)));
        }
        return result.OrderBy(r => r.FullName).ToArray();
    }

    public Task<ServiceOrderDto> StartRepairAsync(string code, StartSourceRepair input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ReadActorAsync(token); Validate(input);
            var request = await requests.GetAsync(code, true, token) ?? throw Missing();
            RequireVisible(request, actor);
            var hash = CommandHash(request.DisplayCode, input);
            if (await ReplayCommandAsync(request, input.RequestId, actor.Id, hash, token))
            {
                var prior = (await events.FirstOrDefaultAsync(e => e.CommandId == input.RequestId, token))!;
                var saved = ProcurementJson.Read<StartedExecution>(prior.Payload!);
                return await repairExecution.GetAsync(saved.ExecutionCode, token);
            }
            await EditableAsync(code, input.Version, token);
            await assignment.RequireAsync(code, actor.Id, token);
            if (request.RequestType != ProcurementRequestType.Repair || request.RequiresReview || request.MappingIssue is not null)
                throw Invalid("Select a mapped repair request ready for execution.");
            if (input.Items.Any(i => i is null)) throw Invalid("Repair plan lines cannot be null.");
            foreach (var item in input.Items) Validate(item);
            if (input.Items.Select(i => BusinessCode.Normalize(i.SourceLineCode)).Distinct().Count() != input.Items.Count) throw Invalid("Duplicate source repair lines.");
            var plan = input.Items.ToDictionary(i => BusinessCode.Normalize(i.SourceLineCode) ?? "", StringComparer.Ordinal);
            var equipmentLines = request.Lines.Where(l => l.Kind == ProcurementLineKind.Equipment).ToArray();
            if (equipmentLines.Length == 0 || plan.Count != equipmentLines.Length || equipmentLines.Any(l => !plan.ContainsKey(l.SourceLineCode)))
                throw Invalid("Specify the actual handling location for every source repair line exactly once.");
            var execution = await repairExecution.StartSourceExecutionAsync(new CreateServiceOrderRequest
            {
                RequestId = input.RequestId, SourceRequestCode = request.DisplayCode, OrderType = ServiceOrderType.Repair,
                Notes = request.Notes, Items = equipmentLines.OrderBy(l => l.SourceLineCode).Select(l => new ServiceItemRequest
                {
                    SourceLineCode = l.SourceLineCode, DeviceCode = l.DeviceCode!, DamageReportCode = l.DamageReportCode,
                    ExecutionMethod = plan[l.SourceLineCode].ExecutionMethod, WorkDescription = l.Description,
                    DamageSessionId = plan[l.SourceLineCode].DamageSessionId,
                    OutsideDamageSession = plan[l.SourceLineCode].OutsideDamageSession
                }).ToList()
            }, request.Id, token);
            await CommandEventAsync(request, input.RequestId, actor.Id, hash, "REPAIR_EXECUTION_STARTED", "InProgress", new StartedExecution(execution.DisplayCode), token);
            return execution;
        }, ct);
    private sealed record StartedExecution(string ExecutionCode);
}
