using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.DamageReports;
using CSVC.Core.Devices;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DamageReports;

public sealed partial class DamageReportService
{
    public Task<DamageReportDto> VerifyAsync(string code, VerifyDamageReportRequest request, CancellationToken ct)
        => WriteAsync(async (_, token) =>
        {
            var actor = await ActorAsync(token);
            if (actor.Scope is not null) throw Forbidden();
            Validate(request);
            Key(request.RequestId);
            var reason = Clean(request.Reason) ?? throw Invalid("A verification reason is required.");
            if (request.Outcome is not (DamageVerificationStatus.ConfirmedDamaged or DamageVerificationStatus.ConfirmedNotDamaged))
                throw Invalid("Select a verification outcome.");
            var report = await ReportAsync(code, actor, true, token);
            var hash = Hash(new { report.ReportCode, request.ExpectedVersion, request.Outcome, reason });
            if (await Reports.RequestAsync(request.RequestId, token) is { } prior)
                return Replay(prior, actor, "VERIFY", hash, report.Id);
            Version(report, request.ExpectedVersion);
            if (report.IncidentType != DamageReportIncidentType.Damage)
                throw Invalid("Verification applies to damage reports.");
            if (report.Decision is not null)
                throw Conflict("REPORT_ALREADY_VERIFIED", "The report has already been verified.");

            var now = Max(clock.GetUtcNow(), Max(report.ReportedAt, report.Reception?.ReceivedAt ?? report.ReportedAt));
            DeviceDamageSession? session = null;
            if (request.Outcome == DamageVerificationStatus.ConfirmedDamaged)
            {
                await inventoryTransaction.LockDevicesAsync([report.DeviceId], token);
                var device = await uow.Devices.GetByIdAsync(report.DeviceId, token) ?? throw Missing();
                if (device.Condition == DeviceCondition.Missing)
                    throw Conflict("REPORT_DEVICE_MISSING", "A missing device cannot enter the damage workflow.");
                session = await damageSessions.FindOpenAsync(device.Id, token);
                if (session is null)
                {
                    session = new() { DeviceId = device.Id, OpenedAt = now, OpenedByAccountId = actor.Id };
                    await sessionRows.AddAsync(session, token);
                }
                if (device.Condition != DeviceCondition.Damaged)
                    await SetDamageConditionAsync(device, DeviceCondition.Damaged, session.Id, report.ReportCode,
                        request.RequestId, actor.Id, now, "DEVICE_DAMAGE_CONFIRMED", token);
            }

            if (report.Pending is { } pending)
            {
                Reports.RemovePending(pending);
                report.Pending = null;
                report.Reception = new()
                {
                    DamageReportId = report.Id, ReceivedByAccountId = actor.Id, ReceivedAt = now, RequestId = request.RequestId
                };
            }

            report.Decision = new()
            {
                DamageReportId = report.Id, DeviceId = report.DeviceId, DamageSessionId = session?.Id,
                Outcome = request.Outcome, Reason = reason, DecidedAt = now, DecidedByAccountId = actor.Id
            };
            report.RowVersion++;
            await AuditAsync(report, actor.Id, request.RequestId, "DAMAGE_REPORT_VERIFIED", ["verificationStatus", "rowVersion"],
                new { status = DamageVerificationStatus.Pending.ToString() },
                new { status = request.Outcome.ToString(), reason, sessionId = session?.Id, report.RowVersion }, token);
            var dto = await DtoAsync(report, token);
            await SaveRequestAsync(request.RequestId, report.Id, actor.Id, "VERIFY", hash, dto, token);
            return dto;
        }, ct);

    public async Task<PagedResult<DamageSessionDto>> SessionsAsync(DamageSessionQuery query, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        Validate(query);
        if (actor.Scope is not null && string.IsNullOrWhiteSpace(query.DeviceCode)) throw Forbidden();
        return await damageSessions.ListAsync(query, actor.Scope, actor.Scope is null, null, ct);
    }

    public async Task<IReadOnlyList<DamageSessionOptionDto>> RepairOptionsAsync(string[] deviceCodes, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (actor.Scope is not null) throw Forbidden();
        if (deviceCodes.Length is < 1 or > 100) throw Invalid("Supply 1 to 100 device codes.");
        return await damageSessions.RepairOptionsAsync(deviceCodes.Select(Code).Distinct().ToArray(), ct);
    }

    public async Task<DamageSessionDto> SessionAsync(Guid sessionId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var page = await damageSessions.ListAsync(new DamageSessionQuery { PageSize = 1 }, actor.Scope, actor.Scope is null, sessionId, ct);
        return page.Items.SingleOrDefault() ?? throw Missing();
    }

    private Task SetDamageConditionAsync(Device device, DeviceCondition condition, Guid sessionId, string sourceCode,
        Guid requestId, Guid actorId, DateTimeOffset at, string action, CancellationToken ct)
    {
        var before = device.Condition;
        device.Condition = condition;
        return uow.DeviceHistories.AddAsync(new DeviceHistory
        {
            DeviceId = device.Id, RequestId = requestId, ActorAccountId = actorId, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = at,
            ChangedFields = JsonSerializer.Serialize(new[] { "condition" }),
            BeforeData = JsonSerializer.Serialize(new { condition = before.ToString() }),
            AfterData = JsonSerializer.Serialize(new { condition = condition.ToString(), sessionId, sourceCode })
        }, ct);
    }
}
