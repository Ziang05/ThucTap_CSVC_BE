using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.DamageReports;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DamageReports;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReportImageRequest
{
    [Required, StringLength(255)] public string Filename { get; set; } = "";
    [Required, StringLength(7_000_000)] public string DataBase64 { get; set; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateDamageReportRequest
{
    public Guid RequestId { get; set; }
    [Required, StringLength(32)] public string DeviceCode { get; set; } = "";
    [Required, RegularExpression("^(DAMAGE|ABNORMAL|MISSING)$")] public string IncidentType { get; set; } = "";
    [Required, StringLength(10000)] public string IncidentDescription { get; set; } = "";
    public DateTimeOffset? DetectedAt { get; set; }
    [StringLength(4000)] public string? Notes { get; set; }
    [MaxLength(5)] public List<ReportImageRequest> Images { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDamageReportRequest
{
    public Guid RequestId { get; set; }
    [Range(1, int.MaxValue)] public int ExpectedVersion { get; set; }
    [StringLength(32)] public string? DeviceCode { get; set; }
    [RegularExpression("^(DAMAGE|ABNORMAL|MISSING)$")] public string? IncidentType { get; set; }
    [StringLength(10000)] public string? IncidentDescription { get; set; }
    public DateTimeOffset? DetectedAt { get; set; }
    private string? _notes;
    [StringLength(4000)] public string? Notes { get => _notes; set { _notes = value; NotesSpecified = true; } }
    [JsonIgnore] public bool NotesSpecified { get; private set; }
    [MaxLength(5)] public List<ReportImageRequest> AddImages { get; set; } = [];
    [MaxLength(5)] public List<int> RemoveImages { get; set; } = [];
    [MaxLength(5)] public List<ReplaceReportImageRequest> ReplaceImages { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReplaceReportImageRequest
{
    [Range(1, int.MaxValue)] public int SequenceNo { get; set; }
    [Required] public ReportImageRequest Image { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReceiveDamageReportRequest
{
    public Guid RequestId { get; set; }
    [Range(1, int.MaxValue)] public int ExpectedVersion { get; set; }
}

public sealed class DamageReportListQuery
{
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Search { get; set; }
    [RegularExpression("^(DAMAGE|ABNORMAL|MISSING)$")] public string? IncidentType { get; set; }
    [RegularExpression("^(NOT_RECEIVED|RECEIVED)$")] public string? ReceptionStatus { get; set; }
    [StringLength(32)] public string? DepartmentCode { get; set; }
    [StringLength(32)] public string? DeviceCode { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    [RegularExpression("^(reportedAt|reportCode)$")] public string SortBy { get; set; } = "reportedAt";
    public bool Descending { get; set; } = true;
}

public sealed record ReportEvidenceDto(int SequenceNo, string Filename, string ContentType, long SizeBytes, string ContentUrl);
public sealed record ReportHistoryDto(string ActionCode, string ActorAccountCode, DateTimeOffset OccurredAt, string? ChangedFields, string? BeforeData, string? AfterData);
public sealed record DamageReportSummaryDto(string ReportCode, string DeviceCode, string DeviceName, string IncidentType,
    string ReportingDepartment, string ReporterName, DateTimeOffset ReportedAt, string ReceptionStatus, int RowVersion);
public sealed record DamageReportDto(string ReportCode, string DeviceCode, string DeviceName, string? DeviceSerial,
    string DeviceDepartment, string? DeviceAreaPath, DateTimeOffset DeviceContextCapturedAt,
    string ReportingDepartment, string ReporterName, string ReporterAccountCode,
    string IncidentType, string IncidentDescription, DateTimeOffset DetectedAt, string? Notes,
    DateTimeOffset ReportedAt, DateTimeOffset? LastUpdatedAt, string? LastUpdatedByAccountCode, int RowVersion,
    string ReceptionStatus, string? ReceivedByAccountCode, DateTimeOffset? ReceivedAt,
    IReadOnlyList<ReportEvidenceDto> Evidence);
public sealed record ReportDeviceOptionDto(string DeviceCode, string Name, bool CanReport);
public sealed record EvidenceDownload(byte[] Content, string ContentType);
public sealed record ValidatedReportImage(string Filename, string ContentType, byte[] Bytes, string Sha256);

public interface IDamageReportService
{
    Task<PagedResult<DamageReportSummaryDto>> ListAsync(DamageReportListQuery query, CancellationToken ct);
    Task<DamageReportDto> GetAsync(string code, CancellationToken ct);
    Task<DamageReportDto> CreateAsync(CreateDamageReportRequest request, CancellationToken ct);
    Task<DamageReportDto> UpdateAsync(string code, UpdateDamageReportRequest request, CancellationToken ct);
    Task<DamageReportDto> ReceiveAsync(string code, ReceiveDamageReportRequest request, CancellationToken ct);
    Task<PagedResult<ReportHistoryDto>> HistoryAsync(string code, int page, int pageSize, CancellationToken ct);
    Task<EvidenceDownload> EvidenceAsync(string code, int sequence, CancellationToken ct);
    Task<PagedResult<ReportDeviceOptionDto>> DevicesAsync(int page, int pageSize, string? search, CancellationToken ct);
}

public interface IReportEvidenceStore
{
    Task<string> StoreAsync(ValidatedReportImage image, CancellationToken ct);
    Task<byte[]?> ReadAsync(string key, CancellationToken ct);
    Task DeleteUncommittedAsync(string key, CancellationToken ct);
}
