using System.Text;
using System.Text.RegularExpressions;

namespace CSVC.Core.Common;

public static class TextNormalization
{
    public static string Email(string value) => value.Trim().ToLowerInvariant();

    public static string Name(string value)
        => Regex.Replace(value.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
}
