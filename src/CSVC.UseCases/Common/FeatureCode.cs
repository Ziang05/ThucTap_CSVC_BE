namespace CSVC.UseCases.Common;

// Codes for the additional procurement/material records. Existing business records
// continue to use the pulled IBusinessCodeGenerator and its dated sequences.
internal static class FeatureCode
{
    public static string New(string prefix)
        => prefix + Guid.NewGuid().ToString("N")[..(32 - prefix.Length)].ToUpperInvariant();
}
