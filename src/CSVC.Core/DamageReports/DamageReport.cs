using CSVC.Core.Common;

namespace CSVC.Core.DamageReports;

public enum DamageReportIncidentType { Damage, Abnormal, Missing }

public sealed class DamageReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ReportCode { get; set; }
    public Guid DeviceId { get; set; }
    public Guid ReportingDepartmentId { get; set; }
    public Guid ReporterAccountId { get; set; }
    public DamageReportIncidentType IncidentType { get; set; }
    public required string IncidentDescription { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public Guid? LastUpdatedByAccountId { get; set; }
    public int RowVersion { get; set; } = 1;
    public Guid CreateRequestId { get; set; }
    public required string CreateRequestHash { get; set; }
    public DamageReportContext Context { get; set; } = null!;
    public DamageReportPendingDevice? Pending { get; set; }
    public DamageReportReception? Reception { get; set; }
    public ICollection<DamageReportEvidence> Evidence { get; set; } = new List<DamageReportEvidence>();
}

public sealed class DamageReportContext
{
    public Guid DamageReportId { get; set; }
    public required string ReporterNameSnapshot { get; set; }
    public required string ReportingDepartmentNameSnapshot { get; set; }
    public required string DeviceCodeSnapshot { get; set; }
    public required string DeviceNameSnapshot { get; set; }
    public string? DeviceSerialSnapshot { get; set; }
    public Guid DeviceDepartmentIdAtReport { get; set; }
    public required string DeviceDepartmentNameSnapshot { get; set; }
    public Guid? DeviceAreaIdAtReport { get; set; }
    public string? DeviceAreaPathSnapshot { get; set; }
    public DateTimeOffset DeviceContextCapturedAt { get; set; }
}

public sealed class DamageReportPendingDevice
{
    public Guid DeviceId { get; set; }
    public Guid DamageReportId { get; set; }
}

public sealed class DamageReportReception
{
    public Guid DamageReportId { get; set; }
    public Guid ReceivedByAccountId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid RequestId { get; set; }
}

public sealed class DamageReportHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DamageReportId { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string ActionCode { get; set; }
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;
    public string? ReasonCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? ChangedFields { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}

public sealed class DamageReportEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DamageReportId { get; set; }
    public int SequenceNo { get; set; }
    public required string StorageObjectKey { get; set; }
    public required string OriginalFilename { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256Hex { get; set; }
    public Guid UploadedByAccountId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public Guid? RemovedByAccountId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

// Durable results also cover evidence-only and no-change retries without artificial audit events.
public sealed class DamageReportRequest
{
    public Guid RequestId { get; set; }
    public Guid DamageReportId { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string Operation { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResponseJson { get; set; }
}
