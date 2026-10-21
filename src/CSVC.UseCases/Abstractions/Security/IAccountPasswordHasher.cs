namespace CSVC.UseCases.Abstractions.Security;

public interface IAccountPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
