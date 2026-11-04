using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DeviceLoans;
using CSVC.Core.Devices;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceLoans.DTOs;

namespace CSVC.UseCases.DeviceLoans;

public sealed partial class DeviceLoanService
{
    public async Task<PagedResult<LoanDeviceOptionDto>> EligibleDevicesAsync(LoanDeviceOptionsQuery query, CancellationToken ct)
    {
        var actor = await RequireDepartmentStaffAsync(ct);
        Validate(query);
        return await unitOfWork.Devices.LoanOptionsAsync(query, actor.DepartmentId, ct);
    }

    public Task ReturnAsync(string code, LoanReturnRequest request, CancellationToken ct)
        => ReturnActionAsync(code, request, false, ct);

    public Task ConfirmReturnAsync(string code, LoanReturnRequest request, CancellationToken ct)
        => ReturnActionAsync(code, request, true, ct);

    private async Task ReturnActionAsync(string code, LoanReturnRequest request, bool confirm, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var loan = await LoadForMutationAsync(code, request.Version, actor, token);
            if (actor.DepartmentId != (confirm ? loan.LenderDepartmentId : loan.BorrowerDepartmentId))
                throw Forbidden("Only the appropriate lending or borrowing department may perform this action.");
            if (loan.Status != (confirm ? DeviceLoanStatus.PendingReturnConfirmation : DeviceLoanStatus.OnLoan))
                throw Conflict("INVALID_WORKFLOW_TRANSITION", "The loan is not awaiting this return action.");
            var items = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            await transaction.LockDevicesAsync(items.Select(i => i.DeviceId), token);
            var devices = await LoadLoanDevicesAsync(items, token);
            var inputs = ValidateCompleteItemSet(request.Items, items, devices.ToDictionary(d => d.Id), i => i.DeviceCode);
            if (request.Items.Any(i => i.Condition is null || !Enum.IsDefined(i.Condition.Value) || i.Note?.Length > 1000))
                throw Validation("Supply a valid condition for every device.");
            var before = SnapshotStatus(loan);
            foreach (var item in items)
            {
                var device = devices.Single(d => d.Id == item.DeviceId);
                await RequireLenderDeviceAsync(loan, device, true, token);
                var input = inputs[device.DeviceCode];
                if (confirm)
                {
                    item.ConditionAtReturn = input.Condition;
                    item.ReturnNote = Clean(input.Note);
                    device.Condition = input.Condition!.Value;
                    unitOfWork.Devices.Update(device);
                    if (!await unitOfWork.DeviceOperationHolds.ReleaseAsync(device.DeviceCode, DeviceOperationKind.Loan, loan.Id, token))
                        throw Conflict("DEVICE_BUSY", "The loan no longer owns its reservation.");
                }
                else
                {
                    item.DeclaredReturnCondition = input.Condition;
                    item.DeclaredReturnNote = Clean(input.Note);
                }
                unitOfWork.DeviceLoanItems.Update(item);
            }
            if (confirm) { loan.Status = DeviceLoanStatus.Returned; loan.ReturnAccountId = actor.AccountId; loan.ReturnedAt = clock.GetUtcNow(); }
            else { loan.Status = DeviceLoanStatus.PendingReturnConfirmation; loan.ReturnRequestedByAccountId = actor.AccountId; loan.ReturnRequestedAt = clock.GetUtcNow(); }
            loan.Version++;
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, confirm ? "LOAN_RETURNED" : "LOAN_RETURN_REQUESTED",
                ["status", "returnConditions"], before, Snapshot(loan, request.Items.Select(i => new { i.DeviceCode, i.Condition, i.Note })), token);
            return true;
        }, ct);

    public async Task RejectReturnAsync(string code, LoanDecisionRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var loan = await LoadForMutationAsync(code, request.Version, actor, token);
            if (loan.LenderDepartmentId != actor.DepartmentId) throw Forbidden("Only the lender may reject a return.");
            if (loan.Status != DeviceLoanStatus.PendingReturnConfirmation) throw Conflict("INVALID_WORKFLOW_TRANSITION", "No return is awaiting confirmation.");
            var reason = Clean(request.Reason) ?? throw Validation("A rejection reason is required.");
            var items = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            await transaction.LockDevicesAsync(items.Select(i => i.DeviceId), token);
            foreach (var device in await LoadLoanDevicesAsync(items, token)) await RequireLenderDeviceAsync(loan, device, true, token);
            var before = SnapshotStatus(loan);
            loan.Status = DeviceLoanStatus.OnLoan;
            loan.Version++;
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, "LOAN_RETURN_REJECTED", ["status"], before,
                System.Text.Json.JsonSerializer.Serialize(new { reason, loan.ReturnRequestedAt, loan.ReturnRequestedByAccountId }), token);
            return true;
        }, ct);

    // An actual lender explicitly acknowledges a reviewed open legacy loan. Historical audit is untouched.
    public async Task AdoptLegacyAsync(string code, AdoptLegacyLoanRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(l => l.LoanCode == NormalizeCode(code), token) ?? throw Missing();
            RequireVersion(loan, request.Version);
            if (loan.WorkflowVersion != 1 || loan.Status is not (DeviceLoanStatus.PendingApproval or DeviceLoanStatus.Approved or DeviceLoanStatus.OnLoan))
                throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only open legacy loans can be reviewed.");
            if (loan.BorrowerDepartmentId == actor.DepartmentId) throw Forbidden("The borrower cannot acknowledge the lending side.");
            var items = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            if (items.Count == 0) throw Conflict("LOAN_EMPTY", "The loan has no devices.");
            await transaction.LockDevicesAsync(items.Select(i => i.DeviceId), token);
            var devices = await LoadLoanDevicesAsync(items, token);
            if (devices.Count != items.Count || devices.Any(d => d.DepartmentId != actor.DepartmentId))
                throw Forbidden("Only the department managing every device may acknowledge this loan.");
            var before = SnapshotStatus(loan);
            loan.LenderDepartmentId = actor.DepartmentId;
            foreach (var device in devices) await RequireLenderDeviceAsync(loan, device, loan.Status != DeviceLoanStatus.PendingApproval, token);
            // Store the real review decision, never relabel the historical QTTB decision as lender confirmation.
            if (loan.Status != DeviceLoanStatus.PendingApproval)
            { loan.DecisionAccountId = actor.AccountId; loan.DecidedAt = clock.GetUtcNow(); loan.DecisionReason = request.Evidence.Trim(); }
            loan.WorkflowVersion = 2;
            loan.Version++;
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, "LOAN_WORKFLOW_MIGRATED", ["workflowVersion", "lenderDepartment"], before,
                System.Text.Json.JsonSerializer.Serialize(new { lender = actor.DepartmentId, evidence = request.Evidence.Trim(), status = loan.Status.ToString() }), token);
            return true;
        }, ct);

    private async Task<DeviceLoan> LoadForMutationAsync(string code, int? version, Actor actor, CancellationToken ct)
    {
        var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(l => l.LoanCode == NormalizeCode(code), ct) ?? throw Missing();
        if (actor.RoleCode == RoleCodes.DepartmentStaff && loan.BorrowerDepartmentId != actor.DepartmentId && loan.LenderDepartmentId != actor.DepartmentId)
            throw Missing();
        if (loan.WorkflowVersion != 2 || loan.LenderDepartmentId is null)
            throw Conflict("LOAN_LEGACY_REVIEW_REQUIRED", "The lending department must review this legacy loan before using the new workflow.");
        RequireVersion(loan, version);
        return loan;
    }

    private static void RequireVersion(DeviceLoan loan, int? version)
    {
        if (version is null) throw Validation("The current loan version is required.");
        if (loan.Version != version) throw Conflict("VERSION_CONFLICT", "Reload the loan before continuing.");
    }

    private async Task RequireLenderDeviceAsync(DeviceLoan loan, Device device, bool ownHold, CancellationToken ct)
    {
        if (device.DepartmentId != loan.LenderDepartmentId) throw Conflict("LOAN_LENDER_MISMATCH", "The device no longer belongs to the lender.");
        await RequireActiveDepartmentAsync(loan.BorrowerDepartmentId, ct);
        await RequireLenderAsync(loan.LenderDepartmentId!.Value, ct);
        await reservations.RequireAsync(device, false, ownHold ? DeviceOperationKind.Loan : null, ownHold ? loan.Id : null, ct);
    }

    private async Task RequireActiveDepartmentAsync(Guid id, CancellationToken ct)
    {
        if (!await unitOfWork.Departments.ExistsAsync(d => d.Id == id && d.Status == EntityStatus.Active, ct))
            throw Conflict("DEPARTMENT_UNAVAILABLE", "The department is unavailable.");
    }

    private async Task RequireLenderAsync(Guid id, CancellationToken ct)
    {
        await RequireActiveDepartmentAsync(id, ct);
        var accounts = await unitOfWork.Accounts.FindAsync(a => !a.IsLock && (a.RoleId == 2 || a.RoleId == 3), ct);
        foreach (var account in accounts)
        {
            var profile = await unitOfWork.UserProfiles.FirstOrDefaultAsync(p => p.Id == account.UserProfileId && p.DepartmentId == id, ct);
            if (profile is null) continue;
            if (account.RoleId == 3) return;
            // A mismatched equipment staff account cannot serve as the lender's representative.
            var config = await settings.FirstOrDefaultAsync(s => s.Id == 1 && s.EquipmentDepartmentId == id, ct);
            if (config is not null) return;
        }
        throw Conflict("LENDER_UNAVAILABLE", "The lending department has no active staff able to confirm this loan.");
    }
}
