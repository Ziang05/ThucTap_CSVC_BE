namespace CSVC.UseCases.Abstractions.Security;

public interface ICurrentAccount
{
    Guid? AccountId { get; }
    Guid? SessionId { get; }
}
