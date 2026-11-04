using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DamageReports;
using CSVC.Core.Devices;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.DamageReports;

public sealed class DamageReportService(IUnitOfWork uow, IDamageReportTransaction transaction,
    ICurrentAccount current, TimeProvider clock, IEquipmentDepartmentPolicy equipmentPolicy,
    IReportEvidenceStore files) : IDamageReportService
{
    private IDamageReportRepository Reports => uow.DamageReports;

    public async Task<PagedResult<DamageReportSummaryDto>> ListAsync(DamageReportListQuery query, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        Validate(query);
        if (query.From > query.To) throw Invalid("The date range is invalid.");
        return await Reports.ListAsync(query, actor.Scope, ct);
    }

    public async Task<DamageReportDto> GetAsync(string code, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        return await DtoAsync(await ReportAsync(code, actor, false, ct), ct);
    }

    public async Task<PagedResult<ReportDeviceOptionDto>> DevicesAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        Page(page, pageSize);
        if (search?.Length > 200) throw Invalid("Search is too long.");
        return await Reports.DevicesAsync(page, pageSize, search?.Trim(), actor.Scope, ct);
    }

    public Task<DamageReportDto> CreateAsync(CreateDamageReportRequest request, CancellationToken ct)
        => WriteAsync(async (staged, token) =>
        {
            var actor = await ActorAsync(token);
            Validate(request);
            Key(request.RequestId);
            var images = Images(request.Images);
            var deviceCode = Code(request.DeviceCode);
            var description = Description(request.IncidentDescription);
            var type = Type(request.IncidentType);
            var hash = Hash(new { deviceCode, type, description, detectedAt = request.DetectedAt?.ToUniversalTime(), notes = Clean(request.Notes), images = Fingerprints(images) });
            if (await Reports.RequestAsync(request.RequestId, token) is { } prior)
            {
                var result = Replay(prior, actor, "CREATE", hash, null);
                await ReportAsync(result.ReportCode, actor, false, token);
                return result;
            }
            var now = clock.GetUtcNow();
            var detected = request.DetectedAt?.ToUniversalTime() ?? now;
            if (detected > now) throw Invalid("Detection cannot be later than report creation.");
            var device = await DeviceAsync(deviceCode, actor, token);
            if (await Reports.HasPendingAsync(device.Id, token)) throw Conflict("DEVICE_REPORT_PENDING", "This device already has a report awaiting reception.");
            var report = new DamageReport
            {
                ReportCode = BusinessCode.NewRandom("SC"), DeviceId = device.Id,
                ReportingDepartmentId = actor.DepartmentId, ReporterAccountId = actor.Id,
                IncidentType = type, IncidentDescription = description, Notes = Clean(request.Notes),
                DetectedAt = detected, ReportedAt = now, CreateRequestId = request.RequestId, CreateRequestHash = hash,
                Context = new() { ReporterNameSnapshot = actor.Name, ReportingDepartmentNameSnapshot = actor.DepartmentName,
                    DeviceCodeSnapshot = "", DeviceNameSnapshot = "", DeviceDepartmentNameSnapshot = "" }
            };
            await CaptureAsync(report.Context, device, now, token);
            report.Pending = new() { DeviceId = device.Id, DamageReportId = report.Id };
            foreach (var image in images) await AddImageAsync(report, image, actor.Id, now, staged, token);
            await Reports.AddAsync(report, token);
            await AuditAsync(report, actor.Id, request.RequestId, "DAMAGE_REPORT_CREATED", ["report"], null, Snapshot(report), token);
            if (images.Count > 0) await AuditAsync(report, actor.Id, request.RequestId, "EVIDENCE_ADDED", ["evidence"], null, EvidenceSnapshot(report.Evidence), token);
            var dto = await DtoAsync(report, token);
            await SaveRequestAsync(request.RequestId, report.Id, actor.Id, "CREATE", hash, dto, token);
            return dto;
        }, ct);

    public Task<DamageReportDto> ReceiveAsync(string code, ReceiveDamageReportRequest request, CancellationToken ct)
        => WriteAsync(async (_, token) =>
        {
            var actor = await ActorAsync(token);
            if (actor.Scope is not null) throw Forbidden();
            Validate(request); Key(request.RequestId);
            var report = await ReportAsync(code, actor, true, token);
            var hash = Hash(new { report.ReportCode, request.ExpectedVersion });
            if (await Reports.RequestAsync(request.RequestId, token) is { } prior)
                return Replay(prior, actor, "RECEIVE", hash, report.Id);
            Version(report, request.ExpectedVersion);
            if (report.Reception is not null) throw Conflict("REPORT_ALREADY_RECEIVED", "The report has already been received.");
            var before = new { status = "NOT_RECEIVED", report.RowVersion };
            Reports.RemovePending(report.Pending!);
            report.Pending = null;
            report.Reception = new() { DamageReportId = report.Id, ReceivedByAccountId = actor.Id,
                ReceivedAt = Max(clock.GetUtcNow(), report.ReportedAt), RequestId = request.RequestId };
            // The same token covers reception and evidence so stale concurrent edits cannot cross either change.
            report.RowVersion++;
            await AuditAsync(report, actor.Id, request.RequestId, "DAMAGE_REPORT_RECEIVED", ["reception", "rowVersion"], before,
                new { status = "RECEIVED", receivedBy = actor.AccountCode, report.Reception.ReceivedAt, report.RowVersion }, token);
            var dto = await DtoAsync(report, token);
            await SaveRequestAsync(request.RequestId, report.Id, actor.Id, "RECEIVE", hash, dto, token);
            return dto;
        }, ct);

    public Task<DamageReportDto> UpdateAsync(string code, UpdateDamageReportRequest request, CancellationToken ct)
        => WriteAsync(async (staged, token) =>
        {
            var actor = await ActorAsync(token);
            Validate(request); Key(request.RequestId);
            var report = await ReportAsync(code, actor, true, token);
            if (actor.Scope is not null && report.ReporterAccountId != actor.Id) throw Forbidden();
            if (request.RemoveImages is null || request.ReplaceImages is null) throw Invalid("Image operations cannot be null.");
            var additions = Images(request.AddImages);
            var replacements = request.ReplaceImages.Select(x => { Validate(x); return (x.SequenceNo, Image(x.Image)); }).ToList();
            var deviceCode = request.DeviceCode is null ? null : Code(request.DeviceCode);
            var description = request.IncidentDescription is null ? null : Description(request.IncidentDescription);
            var type = request.IncidentType is null ? (DamageReportIncidentType?)null : Type(request.IncidentType);
            var hash = Hash(new { report.ReportCode, request.ExpectedVersion, deviceCode, description, type,
                detectedAt = request.DetectedAt?.ToUniversalTime(), request.NotesSpecified, notes = Clean(request.Notes),
                additions = Fingerprints(additions), remove = request.RemoveImages.Order().ToArray(),
                replacements = replacements.OrderBy(x => x.SequenceNo).Select(x => new { x.SequenceNo, x.Item2.Filename, x.Item2.ContentType, x.Item2.Sha256 }).ToArray() });
            if (await Reports.RequestAsync(request.RequestId, token) is { } prior)
                return Replay(prior, actor, "UPDATE", hash, report.Id);
            Version(report, request.ExpectedVersion);
            var before = Snapshot(report);
            var changed = new List<string>();
            var now = Max(clock.GetUtcNow(), report.ReportedAt);
            if (deviceCode is not null && deviceCode != report.Context.DeviceCodeSnapshot)
            {
                var device = await DeviceAsync(deviceCode, actor, token);
                if (device.Id != report.DeviceId)
                {
                    if (report.Reception is not null || await Reports.HasRepairAsync(report.Id, token))
                        throw Conflict("REPORT_DEVICE_FIXED", "The device cannot change after reception or a repair link.");
                    if (await Reports.HasPendingAsync(device.Id, token)) throw Conflict("DEVICE_REPORT_PENDING", "The target device already has a pending report.");
                    Reports.RemovePending(report.Pending!);
                    report.Pending = null;
                    // Flush deletion before inserting the replacement; the whole operation remains transactional.
                    await uow.CommitAsync(token);
                    report.DeviceId = device.Id;
                    report.Pending = new() { DamageReportId = report.Id, DeviceId = device.Id };
                    await CaptureAsync(report.Context, device, now, token);
                    changed.Add("device");
                }
            }
            if (type is { } incident && incident != report.IncidentType) { report.IncidentType = incident; changed.Add("incidentType"); }
            if (description is not null && description != report.IncidentDescription) { report.IncidentDescription = description; changed.Add("incidentDescription"); }
            if (request.DetectedAt is { } detected)
            {
                if (detected > report.ReportedAt) throw Invalid("Detection cannot be later than report creation.");
                if (detected != report.DetectedAt) { report.DetectedAt = detected.ToUniversalTime(); changed.Add("detectedAt"); }
            }
            if (request.NotesSpecified && Clean(request.Notes) != report.Notes) { report.Notes = Clean(request.Notes); changed.Add("notes"); }
            var removedSequences = request.RemoveImages.Concat(replacements.Select(x => x.SequenceNo)).ToArray();
            if (removedSequences.Any(x => x < 1) || removedSequences.Distinct().Count() != removedSequences.Length)
                throw Invalid("Image operations must have distinct positive sequence numbers.");
            var removed = report.Evidence.Where(x => removedSequences.Contains(x.SequenceNo) && x.RemovedAt is null).ToArray();
            if (removed.Length != removedSequences.Length) throw Conflict("EVIDENCE_CHANGED", "An image is missing or has already been removed.");
            if (report.Evidence.Count(x => x.RemovedAt is null) - removed.Length + additions.Count + replacements.Count > 5)
                throw Invalid("A report supports at most five current images.");
            var imageBefore = EvidenceSnapshot(report.Evidence);
            foreach (var evidence in removed) { evidence.RemovedAt = Max(now, evidence.UploadedAt); evidence.RemovedByAccountId = actor.Id; }
            foreach (var image in additions.Concat(replacements.Select(x => x.Item2)))
                await AddImageAsync(report, image, actor.Id, now, staged, token);
            if (changed.Count > 0 || removed.Length + additions.Count > 0)
            {
                report.RowVersion++;
                report.LastUpdatedAt = now;
                report.LastUpdatedByAccountId = actor.Id;
                if (changed.Count > 0) await AuditAsync(report, actor.Id, request.RequestId, "DAMAGE_REPORT_UPDATED", changed, before, Snapshot(report), token);
                var imageAfter = EvidenceSnapshot(report.Evidence);
                if (additions.Count > 0) await AuditAsync(report, actor.Id, request.RequestId, "EVIDENCE_ADDED", ["evidence"], imageBefore, imageAfter, token);
                if (request.RemoveImages.Count > 0) await AuditAsync(report, actor.Id, request.RequestId, "EVIDENCE_REMOVED", ["evidence"], imageBefore, imageAfter, token);
                if (replacements.Count > 0) await AuditAsync(report, actor.Id, request.RequestId, "EVIDENCE_REPLACED", ["evidence"], imageBefore, imageAfter, token);
            }
            var dto = await DtoAsync(report, token);
            await SaveRequestAsync(request.RequestId, report.Id, actor.Id, "UPDATE", hash, dto, token);
            return dto;
        }, ct);

    public async Task<PagedResult<ReportHistoryDto>> HistoryAsync(string code, int page, int pageSize, CancellationToken ct)
    {
        var actor = await ActorAsync(ct); Page(page, pageSize);
        var report = await ReportAsync(code, actor, false, ct);
        return await Reports.HistoryAsync(report.Id, page, pageSize, ct);
    }

    public async Task<EvidenceDownload> EvidenceAsync(string code, int sequence, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var report = await ReportAsync(code, actor, false, ct);
        var evidence = report.Evidence.SingleOrDefault(x => x.SequenceNo == sequence && x.RemovedAt is null) ?? throw Missing();
        var bytes = await files.ReadAsync(evidence.StorageObjectKey, ct) ?? throw Missing();
        if (bytes.LongLength != evidence.SizeBytes || Convert.ToHexString(SHA256.HashData(bytes)) != evidence.Sha256Hex)
            throw new UseCaseException(FailureKind.Unavailable, "EVIDENCE_UNAVAILABLE", "The evidence is unavailable.");
        return new(bytes, evidence.ContentType);
    }

    private async Task<DamageReportDto> WriteAsync(Func<List<string>, CancellationToken, Task<DamageReportDto>> action, CancellationToken ct)
    {
        var staged = new List<string>();
        try { return await transaction.ExecuteAsync(token => action(staged, token), ct); }
        catch
        {
            // Preserve bytes only when COMMIT may have succeeded despite a transport failure.
            if (transaction.CanDiscardFiles)
                foreach (var key in staged) await files.DeleteUncommittedAsync(key, CancellationToken.None);
            throw;
        }
    }

    private async Task<Actor> ActorAsync(CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw Unauthorized();
        var account = await uow.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct) ?? throw Unauthorized();
        if (!await uow.AuthSessions.ExistsAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct)) throw Unauthorized();
        var role = await uow.Roles.FirstOrDefaultAsync(x => x.Id == account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff)) throw Forbidden();
        var profile = await uow.UserProfiles.FirstOrDefaultAsync(x => x.Id == account.UserProfileId, ct) ?? throw Unauthorized();
        var department = await uow.Departments.FirstOrDefaultAsync(x => x.Id == profile.DepartmentId, ct) ?? throw Forbidden();
        if (role.Code != RoleCodes.DepartmentStaff) await equipmentPolicy.RequireMemberAsync(role.Code, profile.DepartmentId, ct);
        return new(id, account.AccountCode, profile.FullName, profile.DepartmentId, department.Name,
            role.Code == RoleCodes.DepartmentStaff ? profile.DepartmentId : null);
    }

    private async Task<DamageReport> ReportAsync(string code, Actor actor, bool tracked, CancellationToken ct)
    {
        var report = await Reports.GetAggregateAsync(Code(code), actor.Scope, tracked, ct) ?? throw Missing();
        if ((report.Pending is null) == (report.Reception is null) || report.Pending is { } pending && pending.DeviceId != report.DeviceId || report.Context is null)
            throw Conflict("REPORT_STATE_INVALID", "The report has inconsistent reception data.");
        return report;
    }

    private async Task<Device> DeviceAsync(string code, Actor actor, CancellationToken ct)
    {
        var device = await uow.Devices.FirstOrDefaultAsync(x => x.DeviceCode == code && (actor.Scope == null || x.DepartmentId == actor.DepartmentId), ct) ?? throw Missing();
        // All incident types, including missing, are valid; condition/status are never changed here.
        return device;
    }

    private async Task CaptureAsync(DamageReportContext context, Device device, DateTimeOffset now, CancellationToken ct)
    {
        var department = await uow.Departments.FirstOrDefaultAsync(x => x.Id == device.DepartmentId, ct) ?? throw Missing();
        context.DeviceCodeSnapshot = device.DeviceCode; context.DeviceNameSnapshot = device.Name;
        context.DeviceSerialSnapshot = device.SerialNumber; context.DeviceDepartmentIdAtReport = device.DepartmentId;
        context.DeviceDepartmentNameSnapshot = department.Name; context.DeviceAreaIdAtReport = device.AreaId;
        var names = new List<string>(); var seen = new HashSet<Guid>(); var areaId = device.AreaId;
        while (areaId is { } id)
        {
            if (!seen.Add(id)) throw Conflict("AREA_STATE_INVALID", "The device location is inconsistent.");
            var area = await uow.Areas.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            names.Add(area.Name); areaId = area.ParentId;
        }
        names.Reverse(); context.DeviceAreaPathSnapshot = names.Count == 0 ? null : string.Join(" / ", names);
        context.DeviceContextCapturedAt = now;
    }

    private async Task AddImageAsync(DamageReport report, ValidatedReportImage image, Guid actor, DateTimeOffset now, List<string> staged, CancellationToken ct)
    {
        var key = await files.StoreAsync(image, ct); staged.Add(key);
        report.Evidence.Add(new() { DamageReportId = report.Id, SequenceNo = report.Evidence.Select(x => x.SequenceNo).DefaultIfEmpty().Max() + 1,
            StorageObjectKey = key, OriginalFilename = image.Filename, ContentType = image.ContentType, SizeBytes = image.Bytes.LongLength,
            Sha256Hex = image.Sha256, UploadedByAccountId = actor, UploadedAt = now });
    }

    private async Task<DamageReportDto> DtoAsync(DamageReport r, CancellationToken ct)
    {
        async Task<string?> AccountCode(Guid? id) => id is null ? null : (await uow.Accounts.FirstOrDefaultAsync(x => x.Id == id, ct))?.AccountCode;
        var c = r.Context;
        return new(r.ReportCode, c.DeviceCodeSnapshot, c.DeviceNameSnapshot, c.DeviceSerialSnapshot, c.DeviceDepartmentNameSnapshot,
            c.DeviceAreaPathSnapshot, c.DeviceContextCapturedAt, c.ReportingDepartmentNameSnapshot, c.ReporterNameSnapshot,
            (await AccountCode(r.ReporterAccountId))!, Incident(r.IncidentType), r.IncidentDescription, r.DetectedAt, r.Notes,
            r.ReportedAt, r.LastUpdatedAt, await AccountCode(r.LastUpdatedByAccountId), r.RowVersion,
            r.Reception is null ? "NOT_RECEIVED" : "RECEIVED", await AccountCode(r.Reception?.ReceivedByAccountId), r.Reception?.ReceivedAt,
            r.Evidence.Where(x => x.RemovedAt is null).OrderBy(x => x.SequenceNo).Select(x => new ReportEvidenceDto(x.SequenceNo,
                x.OriginalFilename, x.ContentType, x.SizeBytes, $"/api/v1/damage-reports/{Uri.EscapeDataString(r.ReportCode)}/evidence/{x.SequenceNo}")).ToArray());
    }

    private Task SaveRequestAsync(Guid request, Guid report, Guid actor, string operation, string hash, DamageReportDto dto, CancellationToken ct)
        => Reports.AddRequestAsync(new() { RequestId = request, DamageReportId = report, ActorAccountId = actor,
            Operation = operation, PayloadHash = hash, ResponseJson = JsonSerializer.Serialize(dto) }, ct);

    private static DamageReportDto Replay(DamageReportRequest prior, Actor actor, string operation, string hash, Guid? report)
    {
        if (prior.ActorAccountId != actor.Id || prior.Operation != operation || prior.PayloadHash != hash || report is { } id && prior.DamageReportId != id)
            throw Conflict("IDEMPOTENCY_CONFLICT", "The request key has already been used for a different request.");
        return JsonSerializer.Deserialize<DamageReportDto>(prior.ResponseJson)!;
    }

    private Task AuditAsync(DamageReport r, Guid actor, Guid request, string action, IReadOnlyList<string> fields, object? before, object? after, CancellationToken ct)
        => Reports.AddHistoryAsync(new() { DamageReportId = r.Id, ActorAccountId = actor, RequestId = request, ActionCode = action,
            OccurredAt = Max(clock.GetUtcNow(), r.ReportedAt), ChangedFields = JsonSerializer.Serialize(fields),
            BeforeData = before is null ? null : JsonSerializer.Serialize(before), AfterData = after is null ? null : JsonSerializer.Serialize(after) }, ct);

    private static object Snapshot(DamageReport r) => new { r.ReportCode, deviceCode = r.Context.DeviceCodeSnapshot,
        deviceName = r.Context.DeviceNameSnapshot, deviceSerial = r.Context.DeviceSerialSnapshot,
        deviceDepartment = r.Context.DeviceDepartmentNameSnapshot, deviceAreaPath = r.Context.DeviceAreaPathSnapshot,
        r.Context.DeviceContextCapturedAt, incidentType = Incident(r.IncidentType), r.IncidentDescription, r.DetectedAt, r.Notes, r.RowVersion };
    private static object EvidenceSnapshot(IEnumerable<DamageReportEvidence> images) => images.OrderBy(x => x.SequenceNo)
        .Select(x => new { x.SequenceNo, x.OriginalFilename, x.ContentType, x.SizeBytes, x.RemovedAt }).ToArray();
    private static object Fingerprints(IEnumerable<ValidatedReportImage> images) => images.Select(x => new { x.Filename, x.ContentType, x.Sha256 }).ToArray();
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static List<ValidatedReportImage> Images(List<ReportImageRequest>? images)
    {
        if (images is null || images.Count > 5) throw Invalid("At most five images are allowed.");
        return images.Select(Image).ToList();
    }
    private static ValidatedReportImage Image(ReportImageRequest request)
    {
        Validate(request);
        if (request.Filename.Contains('/') || request.Filename.Contains('\\') || request.Filename.Any(char.IsControl)) throw Invalid("Invalid image filename.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.DataBase64); }
        catch (FormatException) { throw Invalid("Invalid image encoding."); }
        if (bytes.Length is < 16 or > 5 * 1024 * 1024) throw Invalid("Images must be between 16 bytes and 5 MiB.");
        var mime = ReportImageValidation.Detect(bytes) ?? throw Invalid("Only valid PNG and JPEG images are supported.");
        return new(request.Filename.Trim(), mime, bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public static string Incident(DamageReportIncidentType type) => type.ToString().ToUpperInvariant();
    private static DamageReportIncidentType Type(string type) => type switch { "DAMAGE" => DamageReportIncidentType.Damage, "ABNORMAL" => DamageReportIncidentType.Abnormal, "MISSING" => DamageReportIncidentType.Missing, _ => throw Invalid("Invalid incident type.") };
    private static string Description(string value) => string.IsNullOrWhiteSpace(value) ? throw Invalid("Description is required.") : value.Trim();
    private static string Code(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 32 ? throw Invalid("Invalid business code.") : value.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Validate(object? value) { if (value is null || !Validator.TryValidateObject(value, new ValidationContext(value), [], true)) throw Invalid("Request validation failed."); }
    private static void Key(Guid id) { if (id == Guid.Empty) throw Invalid("A nonempty requestId is required."); }
    private static void Page(int page, int size) { if (page is < 1 or > 1000000 || size is < 1 or > 100) throw Invalid("Invalid pagination."); }
    private static void Version(DamageReport r, int expected) { if (r.RowVersion != expected) throw Conflict("REPORT_VERSION_CONFLICT", $"Reload the report; current version is {r.RowVersion}."); }
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Forbidden() => new(FailureKind.Forbidden, "REPORT_ACTION_FORBIDDEN", "This report action is not permitted.");
    private static UseCaseException Missing() => new(FailureKind.NotFound, "REPORT_NOT_FOUND", "The requested report or resource was not found.");
    private static UseCaseException Invalid(string message) => new(FailureKind.Validation, "REPORT_VALIDATION_FAILED", message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    private sealed record Actor(Guid Id, string AccountCode, string Name, Guid DepartmentId, string DepartmentName, Guid? Scope);
}
