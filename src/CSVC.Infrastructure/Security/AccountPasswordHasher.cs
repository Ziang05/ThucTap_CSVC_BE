using CSVC.UseCases.Abstractions.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CSVC.Infrastructure.Security;

public sealed class AccountPasswordHasher : IAccountPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private static readonly object User = new();
    private static readonly Lazy<string> DummyHash = new(() => new AccountPasswordHasher().Hash("dummy-login-work-factor"));

    public string Hash(string password) => _hasher.HashPassword(User, password);

    public bool VerifyForLogin(string? hash, string password)
    {
        var fallback = DummyHash.Value;
        return Verify(hash ?? fallback, password);
    }

    public bool Verify(string hash, string password)
    {
        try { return _hasher.VerifyHashedPassword(User, hash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }
}
