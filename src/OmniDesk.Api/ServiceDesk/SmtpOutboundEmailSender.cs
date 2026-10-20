using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace OmniDesk.Api.ServiceDesk;

public sealed class SmtpOutboundEmailSender(IOptions<OutboundEmailOptions> options) : IOutboundEmailSender
{
    private readonly OutboundEmailOptions settings = options.Value;

    public async Task SendAsync(OutboundEmailMessage outgoing, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || !settings.IsValid())
            throw new InvalidOperationException("Outbound email is disabled or not configured.");
        if (!MailboxAddress.TryParse(outgoing.Recipient, out var recipient) ||
            outgoing.Subject.Any(char.IsControl) || outgoing.MessageId.Any(char.IsControl) || outgoing.TextBody.Any(c => c == '\0'))
            throw new InvalidOperationException("Outbound email message is invalid.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, new System.Net.Mail.MailAddress(settings.FromAddress).Address));
        message.To.Add(recipient);
        message.Subject = outgoing.Subject;
        message.MessageId = outgoing.MessageId;
        message.Body = new TextPart("plain") { Text = outgoing.TextBody };

        using var client = new SmtpClient();
        var security = settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            var password = (await File.ReadAllTextAsync(settings.PasswordFile, cancellationToken)).TrimEnd('\r', '\n');
            if (password.Length == 0) throw new InvalidOperationException("SMTP password secret is empty.");
            await client.AuthenticateAsync(settings.Username, password, cancellationToken);
        }
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
