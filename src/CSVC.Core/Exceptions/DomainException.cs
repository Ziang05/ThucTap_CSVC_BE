namespace CSVC.Core.Exceptions;

/// <summary>A domain rule rejected an operation. Messages must be safe for clients.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
