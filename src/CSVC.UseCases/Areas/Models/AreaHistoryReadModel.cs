using CSVC.UseCases.History.Models;

namespace CSVC.UseCases.Areas.Models;

public sealed class AreaHistoryReadModel : HistoryReadModel
{
    public Guid? AreaId { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
