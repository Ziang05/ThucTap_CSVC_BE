using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.Common;

namespace CSVC.UseCases.Suppliers.DTOs;

public sealed class SupplierListItemDto
{
    public string DisplayCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public EntityStatus Status { get; init; }
    public int? ReceiptCount => null;
}

public sealed class SupplierDetailDto
{
    public string DisplayCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public EntityStatus Status { get; init; }
    public int? ReceiptCount => null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Address { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phone { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Version { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SupplierManagementDto? Management { get; set; }
}

public sealed class SupplierManagementDto
{
    public string? CreatedByAccountCode { get; init; }
    public string? CreatedByFullName { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? UpdatedByAccountCode { get; init; }
    public string? UpdatedByFullName { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class SupplierLockImpactDto
{
    public string DisplayCode { get; init; } = "";
    public EntityStatus Status { get; init; }
    public int? ReceiptCount { get; init; }
    public bool LinkCountsComplete => ReceiptCount.HasValue;
    public string Warning => "Locking prevents new supplier assignments; existing receipt links are preserved.";
}

public class SupplierHistoryListItemDto
{
    public Guid Id { get; init; }
    public string? DisplayCode { get; init; }
    public string? Name { get; init; }
    public string ActionCode { get; init; } = "";
    public AuditOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
}

public sealed class SupplierHistoryDetailDto : SupplierHistoryListItemDto
{
    public string? ReasonCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? RequestId { get; set; }
    public IReadOnlyList<string>? ChangedFields { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? BeforeData { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? AfterData { get; set; }
}
