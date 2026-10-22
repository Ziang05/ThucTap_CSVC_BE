namespace CSVC.UseCases.Abstractions.Persistence;

public interface IBusinessCodeGenerator
{
    // Called inside the corresponding write transaction; allocated numbers may have gaps.
    Task<string> NextAccountCodeAsync(CancellationToken cancellationToken);
    Task<string> NextDepartmentCodeAsync(CancellationToken cancellationToken);
}
