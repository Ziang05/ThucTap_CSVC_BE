namespace CSVC.Core.Common;

public static class BusinessCode
{
    public const int MaxLength = 32;
    public const string Pattern = @"^[A-Z0-9][A-Z0-9._/-]*$";

    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
