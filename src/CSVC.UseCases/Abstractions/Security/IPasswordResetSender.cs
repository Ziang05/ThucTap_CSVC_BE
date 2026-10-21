namespace CSVC.UseCases.Abstractions.Security;

public interface IPasswordResetSender
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
