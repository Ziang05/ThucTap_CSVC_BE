using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using CSVC.Core.Common;
using CSVC.Core.ProcurementRequests;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.ProcurementRequests;

namespace CSVC.Infrastructure.ProcurementRequests;

public sealed class SimulatedPmmsSource : IPmmsRequestSource
{
    public string Source => "PMMS_SIMULATED";
    public PmmsSimulatedEvent Normalize(PmmsSimulatedEvent input)
    {
        Check(input);
        if (input.EventId == Guid.Empty) throw Invalid("An eventId is required.");
        var externalCode = Code(input.ExternalCode);
        if (input.Action == PmmsEventAction.Cancel)
        {
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Request is not null) throw Invalid("Cancellation requires a reason and no replacement request payload.");
            return new() { EventId = input.EventId, ExternalCode = externalCode, SourceVersion = input.SourceVersion, Action = input.Action, Reason = Clean(input.Reason) };
        }
        var payload = input.Request ?? throw Invalid("A request payload is required.");
        Check(payload);
        if (payload.CreatedAt == default || payload.UpdatedAt < payload.CreatedAt) throw Invalid("Provide valid source creation and update times.");
        var lines = new List<PmmsRequestLine>();
        foreach (var line in payload.Lines)
        {
            if (line is null) throw Invalid("Source lines cannot be null.");
            Check(line);
            if (line.Kind == ProcurementLineKind.Equipment && (line.Quantity != decimal.Truncate(line.Quantity) || line.Quantity > 200)) throw Invalid("Equipment quantities must be whole numbers up to 200.");
            if (payload.RequestType == ProcurementRequestType.Repair && (line.Kind != ProcurementLineKind.Equipment || line.Quantity != 1)) throw Invalid("Each repair line describes one target device.");
            lines.Add(new() { SourceLineCode = Code(line.SourceLineCode), Kind = line.Kind, Description = TextNormalization.Name(line.Description),
                Quantity = line.Quantity, Unit = TextNormalization.Name(line.Unit), CategoryCode = OptionalCode(line.CategoryCode), DeviceCode = OptionalCode(line.DeviceCode),
                DamageReportCode = OptionalCode(line.DamageReportCode), Specialization = line.Specialization });
        }
        if (lines.Select(l => l.SourceLineCode).Distinct().Count() != lines.Count) throw Invalid("Source line codes must be unique.");
        if (lines.Where(l => l.Kind == ProcurementLineKind.Equipment).Sum(l => l.Quantity) > 200) throw Invalid("A request supports at most 200 equipment packages.");
        if (payload.RequestType == ProcurementRequestType.Repair && lines.Where(l => l.DeviceCode is not null).GroupBy(l => l.DeviceCode).Any(g => g.Count() > 1)) throw Invalid("A device may appear once in a repair request.");
        return new() { EventId = input.EventId, ExternalCode = externalCode, SourceVersion = input.SourceVersion, Action = input.Action, Reason = Clean(input.Reason),
            Request = new() { RequestType = payload.RequestType, Title = TextNormalization.Name(payload.Title), RequestingDepartmentCode = Code(payload.RequestingDepartmentCode),
                Contact = Clean(payload.Contact), ContractReference = Clean(payload.ContractReference), SupplierReference = Clean(payload.SupplierReference), Notes = Clean(payload.Notes),
                SourceStatus = TextNormalization.Name(payload.SourceStatus), CreatedAt = payload.CreatedAt.ToUniversalTime(), UpdatedAt = payload.UpdatedAt.ToUniversalTime(),
                Lines = lines.OrderBy(l => l.SourceLineCode, StringComparer.Ordinal).ToList() } };
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value);
    private static string? OptionalCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : Code(value);
    private static string Code(string value)
    {
        var code = BusinessCode.Normalize(value) ?? "";
        if (!Regex.IsMatch(code, BusinessCode.Pattern)) throw Invalid("Source codes contain unsupported characters.");
        return code;
    }
    private static void Check(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true)) throw Invalid("The simulated source event contains invalid fields.");
    }
    private static UseCaseException Invalid(string message) => new(FailureKind.Validation, "PMMS_EVENT_INVALID", message);
}
