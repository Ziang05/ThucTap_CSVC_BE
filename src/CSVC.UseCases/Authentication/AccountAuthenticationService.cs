using System.ComponentModel.DataAnnotations;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Authentication.DTOs;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSVC.UseCases.Authentication;

public sealed class AccountAuthenticationService(
    IUnitOfWork unitOfWork, IAccountTransaction transaction, IAccountPasswordHasher passwords,
    ISessionLifecycle lifecycle, IRequestContext requestContext, IAuthenticationClientContext client,
    IOptions<AuthenticationOptions> options, TimeProvider clock, IMapper mapper,
    ILogger<AccountAuthenticationService> logger) : IAccountAuthenticationService
{
    private const int FailureLimit = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true))
            throw new UseCaseException(FailureKind.Validation, "VALIDATION_FAILED", "Login request is invalid.");
        var email = TextNormalization.Email(request.Email);
        var result = await PersistAsync<LoginResponse?>(async ct =>
        {
            var account = await unitOfWork.Accounts.GetByNormalizedEmailAsync(email, ct);
            // Unknown emails perform the same password verification work using a dummy hash.
            var passwordValid = passwords.VerifyForLogin(account?.PasswordHash, request.Password);
            var now = clock.GetUtcNow();
            var throttle = await unitOfWork.AuthLoginThrottles.GetByIdAsync(email, ct);
            if (throttle is null)
            {
                throttle = new AuthLoginThrottle { EmailKey = email };
                await unitOfWork.AuthLoginThrottles.AddAsync(throttle, ct);
            }
            if (throttle.BlockedUntil is { } blockedUntil && blockedUntil > now)
            {
                await AuditAsync("LOGIN_FAILED", AuditOutcome.Denied, account?.Id, null, email,
                    "LOGIN_TEMP_BLOCKED", blockedUntil, now, ct);
                return null;
            }
            if (throttle.BlockedUntil.HasValue || throttle.FirstFailedAt is { } first && now >= first + FailureWindow)
                ClearFailures(throttle);
            if (account?.IsLock == true)
            {
                await AuditAsync("LOGIN_FAILED", AuditOutcome.Denied, account.Id, null, email,
                    "ACCOUNT_LOCKED", null, now, ct);
                return null;
            }
            if (account is null || !passwordValid)
            {
                throttle.FirstFailedAt ??= now;
                throttle.FailedCount++;
                await AuditAsync("LOGIN_FAILED", AuditOutcome.Failed, account?.Id, null, email,
                    "INVALID_CREDENTIALS", null, now, ct);
                if (throttle.FailedCount >= FailureLimit)
                {
                    throttle.BlockedUntil = now + BlockDuration;
                    await AuditAsync("LOGIN_TEMP_BLOCKED", AuditOutcome.Denied, account?.Id, null, email,
                        "LOGIN_TEMP_BLOCKED", throttle.BlockedUntil, now, ct);
                }
                return null;
            }
            var user = await unitOfWork.Accounts.GetDetailAsync(account.Id, now, ct);
            if (user is null || user.RoleCode is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff))
            {
                await AuditAsync("LOGIN_FAILED", AuditOutcome.Denied, account.Id, null, email,
                    "ACCOUNT_NOT_ALLOWED", null, now, ct);
                return null;
            }
            var oldSessions = await unitOfWork.AuthSessions.FindAsync(session => session.AccountId == account.Id && session.IsActive, ct);
            foreach (var old in oldSessions) await lifecycle.EndAsync(old, requestContext.RequestId, account.Id, now, ct);
            // Close the old active row before inserting a replacement under the partial unique index.
            // Both saves remain in the same explicit transaction, including their audit events.
            if (oldSessions.Count > 0) await unitOfWork.CommitAsync(ct);
            var token = SessionToken.Generate();
            var session = new AuthSession
            {
                AccountId = account.Id, SessionTokenHash = SessionToken.Hash(token),
                ExpiresAt = now.AddMinutes(options.Value.SessionLifetimeMinutes)
            };
            await unitOfWork.AuthSessions.AddAsync(session, ct);
            ClearFailures(throttle);
            await AuditAsync("LOGIN_SUCCESS", AuditOutcome.Success, account.Id, session.Id, email, null, null, now, ct);
            await AuditAsync("SESSION_CREATED", AuditOutcome.Success, account.Id, session.Id, email, null, null, now, ct);
            return new LoginResponse
            {
                AccessToken = token, ExpiresAt = session.ExpiresAt, User = mapper.Map<AuthenticatedUserDto>(user)
            };
        }, cancellationToken);
        // Reject only AFTER persisting the failed attempt and counter; throwing inside would roll them back.
        return result ?? throw new UseCaseException(FailureKind.Unauthorized, "INVALID_CREDENTIALS", "Unable to sign in with these credentials.");
    }

    public async Task LogoutAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!SessionToken.IsWellFormed(token)) return;
        var hash = SessionToken.Hash(token!);
        await PersistAsync(async ct =>
        {
            var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(item => item.SessionTokenHash == hash, ct);
            if (session is not null)
                await lifecycle.EndAsync(session, requestContext.RequestId, session.AccountId, clock.GetUtcNow(), ct, "LOGOUT");
            return true;
        }, cancellationToken);
    }

    private async Task<T> PersistAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        try { return await transaction.ExecuteAsync(action, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Authentication persistence failed. RequestId: {RequestId}, ErrorType: {ErrorType}",
                requestContext.RequestId, exception.GetType().Name);
            throw new UseCaseException(FailureKind.Unavailable, "AUTHENTICATION_UNAVAILABLE", "Authentication is temporarily unavailable.");
        }
    }

    private Task AuditAsync(string action, AuditOutcome outcome, Guid? accountId, Guid? sessionId, string email,
        string? reason, DateTimeOffset? blockedUntil, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var userAgent = client.UserAgent;
        if (userAgent is not null) userAgent = new string(userAgent.Take(512).Where(c => !char.IsControl(c)).ToArray());
        return unitOfWork.AuthHistories.AddAsync(new AuthHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = outcome == AuditOutcome.Success ? accountId : null,
            AccountId = accountId, SessionId = sessionId, EmailSubmitted = email,
            ActionCode = action, Outcome = outcome, ReasonCode = reason, BlockedUntil = blockedUntil,
            OccurredAt = now, IpAddress = client.IpAddress, UserAgent = userAgent
        }, cancellationToken);
    }

    private static void ClearFailures(AuthLoginThrottle throttle)
    {
        throttle.FailedCount = 0;
        throttle.FirstFailedAt = null;
        throttle.BlockedUntil = null;
    }
}
