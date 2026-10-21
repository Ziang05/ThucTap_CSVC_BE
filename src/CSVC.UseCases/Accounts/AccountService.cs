using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Common;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;

namespace CSVC.UseCases.Accounts;

public sealed class AccountService(
    IUnitOfWork unitOfWork, IAccountTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, IAccountPasswordHasher passwords, IPasswordResetSender resetSender,
    IMapper mapper, TimeProvider clock, ILogger<AccountService> logger) : IAccountService
{
    public async Task<PagedResult<AccountListItemDto>> ListAsync(AccountListQuery query, CancellationToken cancellationToken = default)
    {
        await RequireActorAsync(null, true, cancellationToken);
        Validate(query);
        return await unitOfWork.Accounts.ListAsync(query, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<AccountDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await RequireActorAsync(id, false, cancellationToken);
        return await unitOfWork.Accounts.GetDetailAsync(id, clock.GetUtcNow(), cancellationToken)
               ?? throw Error(FailureKind.NotFound, "ACCOUNT_NOT_FOUND", "Account not found.");
    }

    public Task<AccountDetailDto> GetCurrentAsync(CancellationToken cancellationToken = default)
        => GetAsync(CurrentId(), cancellationToken);

    public Task<CreatedAccountDto> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        => WriteAsync<CreatedAccountDto>(null, async ct =>
        {
            await RequireActorAsync(null, true, ct);
            Validate(request);
            await RequireDepartmentAsync(request.DepartmentId, ct);
            var role = await unitOfWork.Roles.GetByIdAsync(request.RoleId, ct);
            if (role is null || role.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff))
                throw Error(FailureKind.Validation, "INVALID_ROLE", "Select a supported role.");
            if (await unitOfWork.Accounts.GetByNormalizedEmailAsync(request.Email, ct) is not null)
                throw Error(FailureKind.Conflict, "EMAIL_ALREADY_EXISTS", "Email is already in use.");
            var profile = mapper.Map<UserProfile>(request);
            var account = mapper.Map<Account>(request);
            account.AccountCode = "TK" + account.Id.ToString("N")[..30];
            account.UserProfileId = profile.Id;
            account.PasswordHash = passwords.Hash(account.Email);
            await unitOfWork.UserProfiles.AddAsync(profile, ct);
            await unitOfWork.Accounts.AddAsync(account, ct);
            await AuditAsync("ACCOUNT_CREATED", account.Id, null, new
            {
                account_code = account.AccountCode, email = account.Email, role_id = account.RoleId,
                full_name = profile.FullName, phone = profile.Phone, department_id = profile.DepartmentId, is_lock = false
            }, ["account_code", "email", "role_id", "full_name", "phone", "department_id", "is_lock"], ct);
            return mapper.Map<CreatedAccountDto>(account);
        }, cancellationToken);

    public Task UpdateProfileAsync(Guid id, UpdateAccountProfileRequest request, CancellationToken cancellationToken = default)
        => WriteAsync(id, async ct =>
        {
            var actor = await RequireActorAsync(id, false, ct);
            Validate(request);
            if (request.BirthDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                throw Error(FailureKind.Validation, "INVALID_BIRTH_DATE", "Birth date cannot be in the future.");
            if (request.DepartmentId.HasValue && actor.RoleCode != RoleCodes.EquipmentManager)
                throw Error(FailureKind.Forbidden, "DEPARTMENT_CHANGE_FORBIDDEN", "Only an equipment manager can change the department.");
            var account = await RequireEditableAsync(id, ct);
            var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct)
                          ?? throw Error(FailureKind.Conflict, "PROFILE_MISSING", "Account profile is unavailable.");
            if (request.DepartmentId is { } departmentId) await RequireDepartmentAsync(departmentId, ct);
            var before = ProfileSnapshot(profile);
            var fields = new List<string>();
            if (profile.FullName != TextNormalization.Name(request.FullName)) fields.Add("full_name");
            if (profile.BirthDate != request.BirthDate) fields.Add("birth_date");
            if (profile.Phone != request.Phone?.Trim()) fields.Add("phone");
            var departmentChanged = request.DepartmentId.HasValue && request.DepartmentId != profile.DepartmentId;
            if (departmentChanged) fields.Add("department_id");
            if (fields.Count == 0) return;
            mapper.Map(request, profile);
            if (request.DepartmentId is { } newDepartmentId) profile.DepartmentId = newDepartmentId;
            await AuditAsync(departmentChanged ? "DEPARTMENT_CHANGED" : "PROFILE_UPDATED", id,
                before, ProfileSnapshot(profile), fields, ct);
        }, cancellationToken);

    public Task UpdateCurrentProfileAsync(UpdateAccountProfileRequest request, CancellationToken cancellationToken = default)
        => UpdateProfileAsync(CurrentId(), request, cancellationToken);

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
        => WriteAsync(current.AccountId, async ct =>
        {
            await RequireActorAsync(CurrentId(), false, ct);
            Validate(request);
            var account = await RequireEditableAsync(CurrentId(), ct);
            if (!passwords.Verify(account.PasswordHash, request.CurrentPassword))
                throw Error(FailureKind.Validation, "INCORRECT_CURRENT_PASSWORD", "Current password is incorrect.");
            await ReplacePasswordAsync(account, request.NewPassword, "PASSWORD_CHANGED", ct);
        }, cancellationToken);

    public Task SetPasswordAsync(Guid id, SetAccountPasswordRequest request, CancellationToken cancellationToken = default)
        => WriteAsync(id, async ct =>
        {
            await RequireActorAsync(null, true, ct);
            Validate(request);
            await ReplacePasswordAsync(await RequireEditableAsync(id, ct), request.NewPassword, "PASSWORD_CHANGED", ct);
        }, cancellationToken);

    public Task SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken = default)
        => WriteAsync(id, async ct =>
        {
            await RequireActorAsync(null, true, ct);
            var account = await unitOfWork.Accounts.GetByIdAsync(id, ct)
                          ?? throw Error(FailureKind.NotFound, "ACCOUNT_NOT_FOUND", "Account not found.");
            if (locked && id == CurrentId())
                throw Error(FailureKind.Forbidden, "SELF_LOCK_FORBIDDEN", "You cannot lock your current account.");
            if (account.IsLock == locked) return;
            if (locked)
            {
                var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
                if (role?.Code == RoleCodes.EquipmentManager && await unitOfWork.Accounts.CountActiveManagersAsync(ct) <= 1)
                    throw Error(FailureKind.Conflict, "LAST_MANAGER_LOCK_FORBIDDEN", "The last active equipment manager cannot be locked.");
            }
            var wasLocked = account.IsLock;
            account.IsLock = locked;
            if (locked)
            {
                await EndSessionsAsync(id, ct);
                await CloseResetRequestsAsync(id, ct);
            }
            await AuditAsync(locked ? "ACCOUNT_LOCKED" : "ACCOUNT_UNLOCKED", id,
                new { is_lock = wasLocked }, new { is_lock = locked }, ["is_lock"], ct);
        }, cancellationToken);

    public async Task RequestPasswordResetAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        // Check delivery configuration for every request, including unknown emails.
        if (!resetSender.IsConfigured)
            throw Error(FailureKind.Unavailable, "PASSWORD_RESET_UNAVAILABLE", "Password reset delivery is not configured.");
        var delivery = await WriteAsync<(string Email, string Token, DateTimeOffset ExpiresAt)?>(null, async ct =>
        {
            var account = await unitOfWork.Accounts.GetByNormalizedEmailAsync(request.Email, ct);
            if (account is null || account.IsLock) return null;
            var old = await CloseResetRequestsAsync(account.Id, ct);
            // Release the partial unique index before inserting the replacement, within this transaction.
            if (old) await unitOfWork.CommitAsync(ct);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = clock.GetUtcNow().AddMinutes(30);
            await unitOfWork.PasswordResetRequests.AddAsync(new PasswordResetRequest
            {
                AccountId = account.Id, TokenHash = HashToken(token), ExpiresAt = expiresAt
            }, ct);
            await AuditAsync("PASSWORD_RESET_REQUESTED", account.Id, null, null, [], ct, anonymous: true);
            return (account.Email, token, expiresAt);
        }, cancellationToken);
        if (delivery is not { } message) return;
        try { await resetSender.SendAsync(message.Email, message.Token, message.ExpiresAt, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The HTTP result stays generic; credentials, recipient and SMTP exception text are never logged.
            logger.LogError("Password reset delivery failed. RequestId: {RequestId}, ErrorType: {ErrorType}",
                requestContext.RequestId, exception.GetType().Name);
        }
    }

    public Task CompletePasswordResetAsync(CompletePasswordResetRequest request, CancellationToken cancellationToken = default)
        => WriteAsync(null, async ct =>
        {
            Validate(request);
            var hash = HashToken(request.Token);
            var reset = await unitOfWork.PasswordResetRequests.FirstOrDefaultAsync(
                item => item.TokenHash == hash && !item.IsClosed, ct);
            if (reset is null || reset.ExpiresAt <= clock.GetUtcNow())
                throw Error(FailureKind.Validation, "INVALID_RESET_TOKEN", "The reset code is invalid or expired.");
            var account = await unitOfWork.Accounts.GetByIdAsync(reset.AccountId, ct);
            if (account is null || account.IsLock)
                throw Error(FailureKind.Validation, "INVALID_RESET_TOKEN", "The reset code is invalid or expired.");
            await ReplacePasswordAsync(account, request.NewPassword, "PASSWORD_RESET_COMPLETED", ct, anonymous: true);
        }, cancellationToken);

    private async Task ReplacePasswordAsync(Account account, string password, string action, CancellationToken ct, bool anonymous = false)
    {
        account.PasswordHash = passwords.Hash(password);
        await EndSessionsAsync(account.Id, ct, anonymous);
        await CloseResetRequestsAsync(account.Id, ct);
        // Even the hash is excluded from before/after snapshots and changed_fields.
        await AuditAsync(action, account.Id, null, null, [], ct, anonymous);
    }

    private async Task EndSessionsAsync(Guid accountId, CancellationToken ct, bool anonymous = false)
    {
        var sessions = await unitOfWork.AuthSessions.FindAsync(item => item.AccountId == accountId && item.IsActive, ct);
        var now = clock.GetUtcNow();
        foreach (var session in sessions)
        {
            session.IsActive = false;
            unitOfWork.AuthSessions.Update(session);
            // Defensive for legacy data; normal writes use the same transaction lock and unique index.
            if (await unitOfWork.AuthHistories.ExistsAsync(item => item.SessionId == session.Id && item.ActionCode == "SESSION_ENDED", ct)) continue;
            await unitOfWork.AuthHistories.AddAsync(new AuthHistory
            {
                RequestId = requestContext.RequestId, ActorAccountId = anonymous ? null : current.AccountId,
                AccountId = accountId, SessionId = session.Id, ActionCode = "SESSION_ENDED", Outcome = AuditOutcome.Success,
                OccurredAt = session.ExpiresAt < now ? session.ExpiresAt : now
            }, ct);
        }
    }

    private async Task<bool> CloseResetRequestsAsync(Guid accountId, CancellationToken ct)
    {
        var resets = await unitOfWork.PasswordResetRequests.FindAsync(item => item.AccountId == accountId && !item.IsClosed, ct);
        foreach (var reset in resets) { reset.IsClosed = true; unitOfWork.PasswordResetRequests.Update(reset); }
        return resets.Count > 0;
    }

    private async Task<Account> RequireEditableAsync(Guid id, CancellationToken ct)
    {
        var account = await unitOfWork.Accounts.GetByIdAsync(id, ct)
                      ?? throw Error(FailureKind.NotFound, "ACCOUNT_NOT_FOUND", "Account not found.");
        if (account.IsLock) throw Error(FailureKind.Conflict, "ACCOUNT_LOCKED", "Unlock the account before editing it.");
        return account;
    }

    private async Task RequireDepartmentAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw Error(FailureKind.Validation, "INVALID_DEPARTMENT", "Select an active department.");
        await transaction.LockDepartmentAsync(id, ct);
        var department = await unitOfWork.Departments.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (department is null || department.Status != EntityStatus.Active)
            throw Error(FailureKind.Validation, "INVALID_DEPARTMENT", "Select an active department.");
    }

    private async Task<SessionIdentity> RequireActorAsync(Guid? target, bool managerOnly, CancellationToken ct)
    {
        var id = CurrentId();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(item => item.Id == id && !item.IsLock, ct);
        var now = clock.GetUtcNow();
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(item => item.Id == current.SessionId
            && item.AccountId == id && item.IsActive && item.ExpiresAt > now, ct);
        if (account is null || session is null) throw Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (role is null || role.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff))
            throw Error(FailureKind.Forbidden, "ACCOUNT_ACCESS_FORBIDDEN", "Account access is not permitted.");
        if (role.Code != RoleCodes.EquipmentManager && (managerOnly || (target.HasValue && target != id)))
            throw Error(FailureKind.Forbidden, "ACCOUNT_ACCESS_FORBIDDEN", "Account access is not permitted.");
        return new(id, session.Id, role.Code);
    }

    private Guid CurrentId() => current.AccountId
        ?? throw Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");

    private Task AuditAsync(string action, Guid id, object? before, object? after, IEnumerable<string> fields,
        CancellationToken ct, bool anonymous = false)
        => unitOfWork.AccountHistories.AddAsync(new AccountHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = anonymous ? null : current.AccountId,
            TargetAccountId = id, ActionCode = action, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            ChangedFields = JsonSerializer.Serialize(fields), BeforeData = before is null ? null : JsonSerializer.Serialize(before),
            AfterData = after is null ? null : JsonSerializer.Serialize(after)
        }, ct);

    private async Task<T> WriteAsync<T>(Guid? target, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        try { return await transaction.ExecuteAsync(action, ct); }
        catch (UseCaseException failure)
        {
            await transaction.ExecuteAsync(async token =>
            {
                var actorId = current.AccountId is { } actor && await unitOfWork.Accounts.ExistsAsync(a => a.Id == actor, token) ? (Guid?)actor : null;
                var targetId = target is { } id && await unitOfWork.Accounts.ExistsAsync(a => a.Id == id, token) ? (Guid?)id : null;
                await unitOfWork.AccountHistories.AddAsync(new AccountHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actorId, TargetAccountId = targetId,
                    ActionCode = failure.Kind is FailureKind.Forbidden or FailureKind.Unauthorized ? "DENIED" : "FAILED",
                    Outcome = failure.Kind is FailureKind.Forbidden or FailureKind.Unauthorized ? AuditOutcome.Denied : AuditOutcome.Failed,
                    ReasonCode = failure.Code, OccurredAt = clock.GetUtcNow()
                }, token);
                return true;
            }, ct);
            throw;
        }
    }

    private async Task WriteAsync(Guid? target, Func<CancellationToken, Task> action, CancellationToken ct)
        => await WriteAsync(target, async token => { await action(token); return true; }, ct);

    private static void Validate(object request)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Request data is invalid.");
    }

    private static object ProfileSnapshot(UserProfile profile) => new
    {
        full_name = profile.FullName, birth_date = profile.BirthDate, phone = profile.Phone, department_id = profile.DepartmentId
    };
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static UseCaseException Error(FailureKind kind, string code, string message) => new(kind, code, message);
}
