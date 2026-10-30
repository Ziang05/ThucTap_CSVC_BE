using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceLoans;
using CSVC.Core.Devices;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceLoans.DTOs;
using CSVC.UseCases.DeviceLoans.Models;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.DeviceLoans;

public sealed class DeviceLoanService(
    IUnitOfWork unitOfWork, IInventoryTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, TimeProvider clock) : IDeviceLoanService
{
    public async Task<PagedResult<DeviceLoanDto>> ListAsync(DeviceLoanListQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        Validate(query);
        Guid? scope = actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null;
        var page = await unitOfWork.DeviceLoans.ListAsync(query, scope, actor.AccountId, clock.GetUtcNow(), ct);
        return new(page.Items.Select(ToDto).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<DeviceLoanDto> GetAsync(string loanCode, CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        var loan = await GetScopedAsync(loanCode, actor, ct);
        return await ToDetailAsync(loan, ct);
    }

    public async Task<DeviceLoanDto> CreateAsync(CreateDeviceLoanRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var now = clock.GetUtcNow();
            var (purpose, dueAt, devices) = await ValidateLoanRequestAsync(request, actor, now, [], token);
            var loan = new DeviceLoan
            {
                LoanCode = "MT" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                BorrowerAccountId = actor.AccountId, BorrowerDepartmentId = actor.DepartmentId,
                Purpose = purpose, DueAt = dueAt, CreatorAccountId = actor.AccountId, CreatedAt = now,
                Status = DeviceLoanStatus.PendingApproval, Notes = Clean(request.Notes),
                Items = devices.Select(device => new DeviceLoanItem { DeviceId = device.Id }).ToList()
            };
            await unitOfWork.DeviceLoans.AddAsync(loan, token);
            await AuditAsync(loan, actor.AccountId, "LOAN_CREATED", ["loanCode", "purpose", "dueAt", "items"], null,
                Snapshot(loan, devices.Select(x => new { x.DeviceCode, x.Name })), token);
            await unitOfWork.CommitAsync(token);
            var read = await unitOfWork.DeviceLoans.GetByCodeAsync(loan.LoanCode, actor.DepartmentId, actor.AccountId, clock.GetUtcNow(), token) ?? throw Missing();
            return await ToDetailAsync(read, token);
        }, ct);

    public async Task UpdateAsync(string loanCode, UpdateDeviceLoanRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(x => x.LoanCode == NormalizeCode(loanCode), token) ?? throw Missing();
            RequirePending(loan);
            if (loan.CreatorAccountId != actor.AccountId || loan.BorrowerDepartmentId != actor.DepartmentId)
                throw Forbidden("Only the creator in the borrower department may edit this loan.");
            var oldItems = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            var oldDevices = await LoadLoanDevicesAsync(oldItems, token);
            var before = Snapshot(loan, oldDevices.Select(x => new { x.DeviceCode, x.Name }));
            var (purpose, dueAt, devices) = await ValidateLoanRequestAsync(request, actor, loan.CreatedAt, oldDevices, token);
            var existingByDeviceId = oldItems.ToDictionary(x => x.DeviceId);
            var selectedIds = devices.Select(x => x.Id).ToHashSet();
            foreach (var old in oldItems.Where(x => !selectedIds.Contains(x.DeviceId)))
                await unitOfWork.DeviceLoanItems.DeleteAsync(old, token);
            foreach (var device in devices.Where(x => !existingByDeviceId.ContainsKey(x.Id)))
                await unitOfWork.DeviceLoanItems.AddAsync(new DeviceLoanItem { LoanId = loan.Id, DeviceId = device.Id }, token);
            loan.Purpose = purpose;
            loan.DueAt = dueAt;
            loan.Notes = Clean(request.Notes);
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, "LOAN_UPDATED", ["purpose", "dueAt", "items", "notes"], before,
                Snapshot(loan, devices.Select(x => new { x.DeviceCode, x.Name })), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task DecideAsync(string loanCode, bool approve, LoanDecisionRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireEquipmentAsync(token);
            Validate(request);
            var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(x => x.LoanCode == NormalizeCode(loanCode), token) ?? throw Missing();
            RequirePending(loan);
            var items = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            if (items.Count == 0) throw Conflict("LOAN_EMPTY", "A loan must contain at least one device.");
            var before = SnapshotStatus(loan);
            var ids = items.Select(x => x.DeviceId).ToArray();
            await transaction.LockDevicesAsync(ids, token);
            var devices = await LoadLoanDevicesAsync(items, token);
            if (devices.Count != items.Count) throw Conflict("DEVICE_NOT_AVAILABLE", "A requested device is no longer available.");
            if (approve)
            {
                foreach (var device in devices)
                {
                    ValidateDeviceAvailable(device);
                    var acquired = await unitOfWork.DeviceOperationHolds.TryAcquireAsync(new DeviceOperationHold
                    {
                        DeviceCode = device.DeviceCode, DeviceId = device.Id, OperationKind = DeviceOperationKind.Loan,
                        OperationId = loan.Id, HeldAt = clock.GetUtcNow()
                    }, token);
                    if (!acquired) throw Conflict("DEVICE_BUSY", "A requested device is already held by another operation.");
                }
                loan.Status = DeviceLoanStatus.Approved;
            }
            else loan.Status = DeviceLoanStatus.Rejected;
            loan.DecisionAccountId = actor.AccountId;
            loan.DecidedAt = clock.GetUtcNow();
            loan.DecisionReason = Clean(request.Reason);
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, approve ? "LOAN_APPROVED" : "LOAN_REJECTED", ["status", "decisionAccount", "decidedAt", "decisionReason"], before, SnapshotStatus(loan), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task HandoverCompleteAsync(string loanCode, LoanHandoverRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(x => x.LoanCode == NormalizeCode(loanCode), token) ?? throw Missing();
            if (loan.Status != DeviceLoanStatus.Approved) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only an approved loan can be handed over.");
            if (loan.BorrowerDepartmentId != actor.DepartmentId) throw Missing();
            var items = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            await transaction.LockDevicesAsync(items.Select(x => x.DeviceId), token);
            var devicesBefore = await LoadLoanDevicesAsync(items, token);
            var conditions = ValidateCompleteItemSet(request.Items, items, devicesBefore.ToDictionary(x => x.Id), x => x.DeviceCode);
            if (request.Items.Any(x => !Enum.IsDefined(x.Condition) || x.Condition == DeviceCondition.Missing || x.Note?.Length > 1000))
                throw Validation("Handover condition or note is invalid.");
            var before = Snapshot(loan, items.Select(item =>
            {
                var device = devicesBefore.Single(d => d.Id == item.DeviceId);
                return new { device.DeviceCode, item.ConditionAtHandover, item.HandoverNote };
            }));
            foreach (var item in items)
            {
                var device = await unitOfWork.Devices.GetByIdAsync(item.DeviceId, token) ?? throw Conflict("DEVICE_NOT_AVAILABLE", "A loan device no longer exists.");
                if (!await unitOfWork.DeviceOperationHolds.IsOwnedByAsync(device.DeviceCode, DeviceOperationKind.Loan, loan.Id, token))
                    throw Conflict("DEVICE_BUSY", "The loan no longer owns a device reservation.");
                var input = conditions[device.DeviceCode];
                item.ConditionAtHandover = input.Condition;
                item.HandoverNote = Clean(input.Note);
                device.Condition = input.Condition;
                unitOfWork.DeviceLoanItems.Update(item);
                unitOfWork.Devices.Update(device);
            }
            loan.Status = DeviceLoanStatus.OnLoan;
            loan.HandedOverAt = clock.GetUtcNow();
            loan.HandoverAccountId = actor.AccountId;
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, "LOAN_HANDED_OVER", ["status", "handedOverAt", "handoverAccount", "itemConditions"], before,
                Snapshot(loan, request.Items.Select(x => new { deviceCode = NormalizeCode(x.DeviceCode), condition = x.Condition.ToString(), note = Clean(x.Note) })), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task ReturnAsync(string loanCode, LoanReturnRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(x => x.LoanCode == NormalizeCode(loanCode), token) ?? throw Missing();
            if (loan.Status != DeviceLoanStatus.OnLoan) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only a loan currently on loan can be returned.");
            if (loan.BorrowerDepartmentId != actor.DepartmentId) throw Missing();
            var items = await unitOfWork.DeviceLoanItems.ListTrackedForLoanAsync(loan.Id, token);
            await transaction.LockDevicesAsync(items.Select(x => x.DeviceId), token);
            var devices = await LoadLoanDevicesAsync(items, token);
            var conditions = ValidateCompleteItemSet(request.Items, items, devices.ToDictionary(x => x.Id), x => x.DeviceCode);
            if (request.Items.Any(x => x.Condition is null || !Enum.IsDefined(x.Condition.Value) || x.Note?.Length > 1000))
                throw Validation("Every returned device requires a valid condition and note.");
            var before = Snapshot(loan, items.Select(item =>
            {
                var device = devices.Single(d => d.Id == item.DeviceId);
                return new { device.DeviceCode, item.ConditionAtHandover, item.ConditionAtReturn };
            }));
            foreach (var item in items)
            {
                var device = devices.Single(x => x.Id == item.DeviceId);
                var input = conditions[device.DeviceCode];
                var condition = input.Condition ?? throw Validation("Every returned device requires a condition.");
                item.ConditionAtReturn = condition;
                item.ReturnNote = Clean(input.Note);
                device.Condition = condition;
                unitOfWork.DeviceLoanItems.Update(item);
                unitOfWork.Devices.Update(device);
                if (!await unitOfWork.DeviceOperationHolds.ReleaseAsync(device.DeviceCode, DeviceOperationKind.Loan, loan.Id, token))
                    throw Conflict("DEVICE_BUSY", "The loan no longer owns its device reservation.");
            }
            loan.Status = DeviceLoanStatus.Returned;
            loan.ReturnedAt = clock.GetUtcNow();
            loan.ReturnAccountId = actor.AccountId;
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, "LOAN_RETURNED", ["status", "returnedAt", "returnAccount", "itemConditions"], before,
                Snapshot(loan, request.Items.Select(x => new { deviceCode = NormalizeCode(x.DeviceCode), condition = x.Condition!.Value.ToString(), note = Clean(x.Note) })), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task CancelAsync(string loanCode, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            var loan = await unitOfWork.DeviceLoans.FirstOrDefaultTrackedAsync(x => x.LoanCode == NormalizeCode(loanCode), token) ?? throw Missing();
            RequirePending(loan);
            if (loan.CreatorAccountId != actor.AccountId || loan.BorrowerDepartmentId != actor.DepartmentId) throw Forbidden("Only the loan creator may cancel it.");
            var before = SnapshotStatus(loan);
            loan.Status = DeviceLoanStatus.Cancelled;
            unitOfWork.DeviceLoans.Update(loan);
            await AuditAsync(loan, actor.AccountId, "LOAN_CANCELLED", ["status"], before, SnapshotStatus(loan), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task<PagedResult<DeviceLoanHistoryDto>> HistoryAsync(string loanCode, DeviceLoanHistoryQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        Validate(query);
        var loan = await GetScopedAsync(loanCode, actor, ct);
        var page = await unitOfWork.DeviceLoanHistories.ListForLoanAsync(loan.Id, query, ct);
        return new(page.Items.Select(x => new DeviceLoanHistoryDto(x.ActionCode, x.Outcome.ToString(), x.OccurredAt,
            x.ActorAccountCode, x.ActorFullName, x.ReasonCode,
            actor.RoleCode == RoleCodes.DepartmentStaff ? null : x.ChangedFields,
            actor.RoleCode == RoleCodes.DepartmentStaff ? null : x.BeforeData,
            actor.RoleCode == RoleCodes.DepartmentStaff ? null : x.AfterData)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    private async Task<(string Purpose, DateTimeOffset DueAt, List<Device> Devices)> ValidateLoanRequestAsync(
        CreateDeviceLoanRequest request, Actor actor, DateTimeOffset createdAt, IReadOnlyList<Device> existingDevices, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 200) throw Validation("A loan must contain between 1 and 200 devices.");
        var purpose = TextNormalization.Name(request.Purpose ?? "");
        if (purpose.Length == 0) throw Validation("Purpose is required.");
        var dueAt = request.DueAt.ToUniversalTime();
        var now = clock.GetUtcNow();
        if (dueAt <= now || dueAt <= createdAt) throw Validation("dueAt must be in the future.");
        var existingByCode = existingDevices.ToDictionary(x => x.DeviceCode, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var devices = new List<Device>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.DeviceCode)) throw Validation("Every item requires a deviceCode.");
            var code = NormalizeCode(item.DeviceCode);
            if (code.Length > 32 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9._/-]{0,31}$"))
                throw Validation("Device code format is invalid.");
            if (!seen.Add(code)) throw Validation("A loan cannot contain duplicate device codes.");
            var device = existingByCode.GetValueOrDefault(code)
                ?? await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == code, ct)
                ?? throw new UseCaseException(FailureKind.NotFound, "DEVICE_NOT_FOUND", "A requested device was not found.");
            ValidateDeviceAvailable(device);
            if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(device.Id, ct)) throw Conflict("DEVICE_BUSY", "A requested device is already reserved.");
            foreach (var member in await unitOfWork.Devices.GetSubtreeAsync(device.Id, ct))
            {
                if (devices.Any(x => x.Id == member.Id)) continue;
                ValidateDeviceAvailable(member);
                if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(member.Id, ct)) throw Conflict("DEVICE_BUSY", "A device in the selected subtree is already reserved.");
                devices.Add(member);
            }
        }
        return (purpose, dueAt, devices);
    }

    private async Task<List<Device>> LoadLoanDevicesAsync(IEnumerable<DeviceLoanItem> items, CancellationToken ct)
    {
        var result = new List<Device>();
        foreach (var item in items)
        {
            var device = await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.Id == item.DeviceId, ct);
            if (device is not null) result.Add(device);
        }
        return result;
    }

    private static Dictionary<string, T> ValidateCompleteItemSet<T>(IEnumerable<T> inputs, IReadOnlyList<DeviceLoanItem> loanItems,
        IReadOnlyDictionary<Guid, Device> devicesById, Func<T, string> code) where T : class
    {
        var values = inputs.ToArray();
        if (values.Length != loanItems.Count || values.Length == 0) throw Validation("The request must include every loan device exactly once.");
        if (values.Any(x => x is null)) throw Validation("The request cannot contain a null item.");
        var requested = values.Select(x => NormalizeCode(code(x))).ToArray();
        if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Length) throw Validation("The request contains duplicate device codes.");
        if (requested.Any(x => x.Length > 32 || !System.Text.RegularExpressions.Regex.IsMatch(x, "^[A-Z0-9][A-Z0-9._/-]{0,31}$")))
            throw Validation("A device code format is invalid.");
        var actual = loanItems.Select(item => devicesById.TryGetValue(item.DeviceId, out var device)
            ? device.DeviceCode : throw Conflict("DEVICE_NOT_AVAILABLE", "A loan device no longer exists.")).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(requested)) throw Validation("The request must include every loan device exactly once.");
        return values.ToDictionary(x => NormalizeCode(code(x)), StringComparer.Ordinal);
    }

    private static void ValidateDeviceAvailable(Device device)
    {
        if (device.Status != EntityStatus.Active) throw Conflict("DEVICE_LOCKED", "A requested device is locked.");
        if (device.Condition != DeviceCondition.Good) throw Conflict("DEVICE_NOT_AVAILABLE", "A damaged or missing device cannot be loaned.");
    }

    private async Task<Actor> RequireActorAsync(CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw Unauthorized();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw Unauthorized();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff)) throw Forbidden("Loan access is not permitted.");
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct) ?? throw Unauthorized();
        return new(id, role.Code, profile.DepartmentId);
    }

    private async Task<Actor> RequireDepartmentStaffAsync(CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        if (actor.RoleCode != RoleCodes.DepartmentStaff) throw Forbidden("Only department staff may perform this action.");
        return actor;
    }

    private async Task<Actor> RequireEquipmentAsync(CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        if (actor.RoleCode is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff)) throw Forbidden("Only equipment staff may decide loan requests.");
        return actor;
    }

    private async Task<DeviceLoanReadModel> GetScopedAsync(string code, Actor actor, CancellationToken ct)
        => await unitOfWork.DeviceLoans.GetByCodeAsync(NormalizeCode(code), actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null,
            actor.AccountId, clock.GetUtcNow(), ct) ?? throw Missing();

    private async Task<DeviceLoanDto> ToDetailAsync(DeviceLoanReadModel read, CancellationToken ct)
    {
        var items = await unitOfWork.DeviceLoanItems.ListForLoanAsync(read.Id, ct);
        return ToDto(read) with { Items = items.Select(x => new DeviceLoanItemDto(x.DeviceCode, x.Name, x.CategoryCode, x.CategoryName,
            x.ConditionAtHandover, x.HandoverNote, x.ConditionAtReturn, x.ReturnNote)).ToArray() };
    }

    private DeviceLoanDto ToDto(DeviceLoanReadModel x)
        => new(x.LoanCode, x.BorrowerAccountCode, x.BorrowerFullName, x.DepartmentCode, x.DepartmentName, x.Purpose,
            x.DueAt, x.IsOverdue ? "Overdue" : x.Status.ToString(), x.IsOverdue, x.CreatorAccountCode, x.CreatedAt,
            x.DecisionAccountCode, x.DecidedAt, x.DecisionReason, x.HandedOverAt, x.HandoverAccountCode,
            x.ReturnedAt, x.ReturnAccountCode, x.Notes, x.ItemCount, x.Version);

    private Task AuditAsync(DeviceLoan loan, Guid actor, string action, IReadOnlyList<string> fields, string? before, string after, CancellationToken ct)
        => unitOfWork.DeviceLoanHistories.AddAsync(new DeviceLoanHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, LoanId = loan.Id, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(), ChangedFields = JsonSerializer.Serialize(fields),
            BeforeData = before, AfterData = after
        }, ct);

    private static string Snapshot(DeviceLoan loan, IEnumerable<object> items)
        => JsonSerializer.Serialize(new { loan.LoanCode, loan.Purpose, loan.DueAt, loan.Notes, items });
    private static string SnapshotStatus(DeviceLoan loan)
        => JsonSerializer.Serialize(new { loan.LoanCode, status = loan.Status.ToString(), loan.DecisionReason, loan.DecidedAt, loan.HandedOverAt, loan.ReturnedAt });
    private static void RequirePending(DeviceLoan loan)
    {
        if (loan.Status != DeviceLoanStatus.PendingApproval) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only a pending loan can be changed or decided.");
    }
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value);
    private static void Validate(object request)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true)) throw Validation("Request validation failed.");
    }
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Forbidden(string message) => new(FailureKind.Forbidden, "WORKFLOW_ACTION_FORBIDDEN", message);
    private static UseCaseException Missing() => new(FailureKind.NotFound, "LOAN_NOT_FOUND", "Loan was not found.");
    private static UseCaseException Validation(string message) => new(FailureKind.Validation, "VALIDATION_FAILED", message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    private sealed record Actor(Guid AccountId, string RoleCode, Guid DepartmentId);
}
