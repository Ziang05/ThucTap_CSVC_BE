using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Common;

namespace CSVC.UseCases.Departments.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class DepartmentWriteRequest
{
    private string _name = "";
    [Required, StringLength(200)]
    public string Name { get => _name; init => _name = TextNormalization.Name(value ?? ""); }
    [StringLength(50)] public string? ShortName { get; init; }
    [StringLength(4000)] public string? Description { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateDepartmentRequest : DepartmentWriteRequest;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDepartmentRequest : DepartmentWriteRequest;

public enum DepartmentSortBy { Name, Code, Status }
public enum DepartmentSortDirection { Asc, Desc }

public sealed class DepartmentListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? Status { get; init; }
    [EnumDataType(typeof(DepartmentSortBy))] public DepartmentSortBy SortBy { get; init; }
    [EnumDataType(typeof(DepartmentSortDirection))] public DepartmentSortDirection SortDirection { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class DepartmentHistoryQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
