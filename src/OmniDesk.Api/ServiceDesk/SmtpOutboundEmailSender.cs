using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text.Json;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.ServiceDesk;

public sealed class SmtpOutboundEmailSender(AppDbContext db, IDataProtectionProvider protection,
    IOptions<OutboundEmailOptions> options) : IOutboundEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var settings = await ReadConfigurationAsync(cancellationToken);
        return settings.Enabled && settings.IsValid();
    }

    public async Task SendAsync(OutboundEmailMessage outgoing, CancellationToken cancellationToken)
    {
        var settings = await ReadConfigurationAsync(cancellationToken);
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
        await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public async Task<OutboundEmailConfiguration> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        var row = await db.OutboundEmailSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (row is not null)
        {
            try
            {
                var json = protection.CreateProtector("OmniDesk.OutboundEmail.Configuration.v1").Unprotect(row.ProtectedConfiguration);
                return JsonSerializer.Deserialize<OutboundEmailConfiguration>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Stored SMTP configuration is empty.");
            }
            catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
            {
                throw new InvalidOperationException("Stored SMTP settings cannot be decrypted.", exception);
            }
        }

        var legacy = options.Value;
        var password = legacy.Enabled && !string.IsNullOrWhiteSpace(legacy.PasswordFile) && File.Exists(legacy.PasswordFile)
            ? (await File.ReadAllTextAsync(legacy.PasswordFile, cancellationToken)).TrimEnd('\r', '\n')
            : "";
        return new OutboundEmailConfiguration(legacy.Enabled, legacy.Host, legacy.Port, legacy.FromAddress,
            legacy.FromName, legacy.DashboardUrl, legacy.Username, password);
    }
}
