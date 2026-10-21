using System.Net.Mail;

namespace OmniDesk.Api.ServiceDesk;

public sealed class OutboundEmailOptions
{
    public const string SectionName = "OutboundEmail";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "OmniDesk";
    public string DashboardUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordFile { get; set; } = "";

    public bool IsValid()
    {
        if (!Enabled) return true;
        if (string.IsNullOrWhiteSpace(Host) || Host.Any(char.IsControl) || Port is not (465 or 587) ||
            !MailAddress.TryCreate(FromAddress, out _) || string.IsNullOrWhiteSpace(FromName) || FromName.Any(char.IsControl) ||
            !Uri.TryCreate(DashboardUrl, UriKind.Absolute, out var dashboard) || dashboard.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(dashboard.Host)) return false;
        return !string.IsNullOrWhiteSpace(Username) && !Username.Any(char.IsControl) &&
            !string.IsNullOrWhiteSpace(PasswordFile) && Path.IsPathFullyQualified(PasswordFile);
    }
}

public sealed record OutboundEmailMessage(string Recipient, string Subject, string TextBody, string MessageId);
public sealed record OutboundEmailConfiguration(bool Enabled, string Host, int Port, string FromAddress,
    string FromName, string DashboardUrl, string Username, string Password)
{
    public bool IsValid() => !Enabled ||
        (!string.IsNullOrWhiteSpace(Host) && !Host.Any(char.IsControl) && Port is 465 or 587 &&
         MailAddress.TryCreate(FromAddress, out _) && !string.IsNullOrWhiteSpace(FromName) &&
         !FromName.Any(char.IsControl) && Uri.TryCreate(DashboardUrl, UriKind.Absolute, out var dashboard) &&
         dashboard.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(dashboard.Host) &&
         !string.IsNullOrWhiteSpace(Username) && !Username.Any(char.IsControl) && !string.IsNullOrEmpty(Password));
}

public interface IOutboundEmailSender
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    Task SendAsync(OutboundEmailMessage message, CancellationToken cancellationToken);
}
