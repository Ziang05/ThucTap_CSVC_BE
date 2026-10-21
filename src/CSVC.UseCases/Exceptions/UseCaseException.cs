namespace CSVC.UseCases.Exceptions;

public enum FailureKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden
}

/// <summary>Expected application failure; code and message must be safe for clients.</summary>
public sealed class UseCaseException(FailureKind kind, string code, string message) : Exception(message)
{
    public FailureKind Kind { get; } = kind;
    public string Code { get; } = code;
}
