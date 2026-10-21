using System.Net;
using System.Net.Mail;
using CSVC.UseCases.Abstractions.Security;
using Microsoft.Extensions.Configuration;

namespace CSVC.Infrastructure.Email;

public sealed class SmtpPasswordResetSender(IConfiguration configuration) : IPasswordResetSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["PasswordReset:Host"])
                                && !string.IsNullOrWhiteSpace(configuration["PasswordReset:From"]);

    public async Task SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(configuration["PasswordReset:Host"], configuration.GetValue("PasswordReset:Port", 587))
        {
            EnableSsl = configuration.GetValue("PasswordReset:EnableSsl", true),
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrEmpty(configuration["PasswordReset:Username"]))
            client.Credentials = new NetworkCredential(configuration["PasswordReset:Username"], configuration["PasswordReset:Password"]);
        using var message = new MailMessage(configuration["PasswordReset:From"]!, email)
        {
            Subject = "CSVC password reset",
            Body = $"Your one-time password reset code is: {token}\nExpires at {expiresAt:O} (UTC).\nIgnore this message if you did not request a reset."
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
