namespace CSVC.UseCases.Abstractions;

public interface IRequestContext
{
    Guid RequestId { get; }
}
