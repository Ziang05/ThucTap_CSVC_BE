using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.DamageReports;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DamageReports;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class VerifyDamageReportRequest
{
    public Guid RequestId { get; init; }
    [Range(1, int.MaxValue)] public int ExpectedVersion { get; init; }
    [EnumDataType(typeof(DamageVerificationStatus))] public DamageVerificationStatus Outcome { get; init; }
    [Required, StringLength(1000)] public string Reason { get; init; } = "";
}

public enum DamageSessionStatus { Open, Closed }

public sealed class DamageSessionQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(32)] public string? DeviceCode { get; init; }
    [EnumDataType(typeof(DamageSessionStatus))] public DamageSessionStatus? Status { get; init; }
    public bool Descending { get; init; } = true;
}

public sealed record DamageRepairAttemptDto(Guid ItemId, string OrderCode, int ItemNo, ServiceWorkflowStatus OrderStatus,
    ServiceItemStatus? ItemStatus, bool AwaitingReturn, DateTimeOffset CreatedAt);

public sealed record DamageSessionInspectionDto(Guid RequestId, Guid ItemId, string OrderCode, int ItemNo, bool Passed,
    string Notes, DateTimeOffset InspectedAt, string InspectedBy);

public sealed record DamageSessionDto(Guid Id, string DeviceCode, string DeviceName, string DepartmentName,
    DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, int Version, IReadOnlyList<string> ReportCodes,
    IReadOnlyList<DamageRepairAttemptDto> Attempts, IReadOnlyList<DamageSessionInspectionDto> Inspections,
    bool AwaitingReturn)
{
    public string? SerialNumber { get; init; }
    public string DepartmentCode { get; init; } = "";
    public DamageSessionRepairBlock? RepairBlock { get; init; }
    public bool CanCreateRepair { get; init; }
}

public enum DamageSessionRepairBlock { ActiveRepair, AwaitingReturn, DeviceBusy, DeviceUnavailable }

public sealed record DamageSessionOptionDto(Guid Id, string DeviceCode, DateTimeOffset OpenedAt, IReadOnlyList<string> ReportCodes);

public interface IDamageSessionRepository
{
    Task<IReadOnlyList<DamageSessionOptionDto>> RepairOptionsAsync(string[] codes, CancellationToken ct);
    Task<DeviceDamageSession?> FindOpenAsync(Guid deviceId, CancellationToken ct);
    Task<bool> HasActiveRepairAsync(Guid sessionId, CancellationToken ct);
    Task<PagedResult<DamageSessionDto>> ListAsync(DamageSessionQuery query, Guid? departmentScope,
        bool includeDetails, Guid? sessionId, CancellationToken ct);
}
