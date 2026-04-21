using System.Net;
using System.Net.Mail;
using AgentFlow.Core.Abstractions;
using AgentFlow.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AgentFlow.Adapters.Notifications;

/// <summary>
/// Implements <see cref="INotifier"/> that sends notifications via SMTP email (Gmail/Outlook supported via SMTP settings).
/// </summary>
public sealed class SmtpEmailNotifier : INotifier
{
    // Services
    private readonly SmtpNotificationConfig _smtp;

    public SmtpEmailNotifier(IOptions<NotificationConfig> cfg)
    {
        _smtp = cfg.Value.Smtp;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(string subject, string body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var message = new MailMessage();
        message.From      = new MailAddress(_smtp.From);
        foreach (var to in _smtp.To ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(to))
                message.To.Add(new MailAddress(to.Trim()));
        }

        message.Subject    = subject ?? string.Empty;
        message.Body       = body ?? string.Empty;
        message.IsBodyHtml = false;

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl   = _smtp.EnableSsl,
            Credentials = new NetworkCredential(_smtp.Username, _smtp.Password)
        };

        // SmtpClient has no CancellationToken API; use ThrowIfCancellationRequested before/after send.
        await client.SendMailAsync(message).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }
}

