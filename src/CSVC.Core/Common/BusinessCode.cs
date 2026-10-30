namespace CSVC.Core.Common;

public static class BusinessCode
{
    public const int MaxLength = 32;
    public const string Pattern = @"^[A-Z0-9][A-Z0-9._/-]*$";

    public static string NewRandom(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (prefix.Length >= MaxLength) throw new ArgumentOutOfRangeException(nameof(prefix));
        return prefix + Guid.NewGuid().ToString("N")[..(MaxLength - prefix.Length)].ToUpperInvariant();
    }

    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
