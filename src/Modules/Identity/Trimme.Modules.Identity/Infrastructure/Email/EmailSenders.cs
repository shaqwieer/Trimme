using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Trimme.Modules.Identity.Infrastructure.Email;

/// <summary>SMTP settings. Bound from <c>Email:Smtp</c>; credentials only ever come from environment variables.</summary>
internal sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    /// <summary>SMTP host; when empty, emails are not sent (only allowed in Development and Testing).</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary><c>None</c> for the local Mailpit container, <c>StartTls</c> or <c>SslOnConnect</c> in production.</summary>
    public string Security { get; set; } = "StartTls";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@trimme.local";

    public string FromName { get; set; } = "TRIMME";
}

internal sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Outbound email transport (spec §9 staff invitations and password resets).</summary>
internal interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>MailKit SMTP sender (Mailpit at <c>mailpit:1025</c> in local compose).</summary>
internal sealed partial class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var security = Enum.TryParse<SecureSocketOptions>(settings.Security, ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host ?? throw new InvalidOperationException("Email:Smtp:Host is not configured."), settings.Port, security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        LogSent(logger, message.Subject.Length);
    }

    // The subject and recipient are not logged: subjects can be localized personal text and addresses are PII.
    [LoggerMessage(Level = LogLevel.Information, Message = "Email sent over SMTP (subject length {SubjectLength})")]
    private static partial void LogSent(ILogger logger, int subjectLength);
}

/// <summary>Development fallback when no SMTP host is configured: the email is dropped (links contain secrets, so nothing is logged).</summary>
internal sealed partial class DroppingEmailSender(ILogger<DroppingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogDropped(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email not sent: Email:Smtp:Host is not configured (start the Mailpit container for local email)")]
    private static partial void LogDropped(ILogger logger);
}
