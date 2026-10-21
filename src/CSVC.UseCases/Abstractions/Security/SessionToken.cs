using System.Security.Cryptography;
using System.Text;

namespace CSVC.UseCases.Abstractions.Security;

public static class SessionToken
{
    public static bool IsWellFormed(string? token)
        => token is { Length: >= 32 and <= 128 } && !token.Any(char.IsWhiteSpace);

    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
