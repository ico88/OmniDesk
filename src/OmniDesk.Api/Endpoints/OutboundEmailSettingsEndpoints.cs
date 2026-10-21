using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Endpoints;

public sealed record OutboundEmailSettingsRequest(bool Enabled, string Host, int Port, string FromAddress,
    string FromName, string DashboardUrl, string Username, string? Password, bool ClearPassword);
public sealed record OutboundEmailTestRequest(string Recipient);

public static class OutboundEmailSettingsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex HostPattern = new("^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$", RegexOptions.CultureInvariant);

    public static void MapOutboundEmailSettingsEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/outbound-email").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var permissions = ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>();
            return await permissions.Allows(ctx.HttpContext.User.UserId(), "administration")
                ? await next(ctx)
                : Results.Forbid();
        });

        admin.MapGet("", async (HttpContext context, AppDbContext db, IDataProtectionProvider protection,
            SmtpOutboundEmailSender sender) =>
        {
            var row = await db.OutboundEmailSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, context.RequestAborted);
            var settings = row is null
                ? await sender.ReadConfigurationAsync(context.RequestAborted)
                : Unprotect(row, protection);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                configured = settings.IsValid() && settings.Enabled,
                enabled = settings.Enabled,
                host = settings.Host,
                port = settings.Port,
                fromAddress = settings.FromAddress,
                fromName = settings.FromName,
                dashboardUrl = settings.DashboardUrl,
                username = settings.Username,
                passwordConfigured = !string.IsNullOrEmpty(settings.Password),
                source = row is null ? "environment" : "dashboard"
            });
        });

        admin.MapPut("", async (OutboundEmailSettingsRequest request, HttpContext context, AppDbContext db,
            IDataProtectionProvider protection, SmtpOutboundEmailSender sender) =>
        {
            var host = request.Host?.Trim() ?? "";
            var fromAddress = request.FromAddress?.Trim() ?? "";
            var fromName = request.FromName?.Trim() ?? "";
            var dashboardUrl = request.DashboardUrl?.Trim() ?? "";
            var username = request.Username?.Trim() ?? "";
            if (host.Length > 253 || host.Length > 0 && !HostPattern.IsMatch(host) ||
                request.Port is not (465 or 587) || fromAddress.Length > 320 ||
                fromAddress.Length > 0 && !MailAddress.TryCreate(fromAddress, out _) ||
                fromName.Length > 128 || fromName.Any(char.IsControl) ||
                dashboardUrl.Length > 2048 || !Uri.TryCreate(dashboardUrl, UriKind.Absolute, out var dashboard) ||
                dashboard.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(dashboard.Host) ||
                username.Length > 320 || username.Any(char.IsControl) || request.Password?.Length > 512 ||
                request.Password?.Any(char.IsControl) == true)
                return Results.BadRequest(new { error = "Controlla host SMTP, porta TLS, mittente, dashboard e credenziali." });

            var row = await db.OutboundEmailSettings.SingleOrDefaultAsync(x => x.Id == 1, context.RequestAborted);
            OutboundEmailConfiguration? previous = row is null
                ? await sender.ReadConfigurationAsync(context.RequestAborted)
                : Unprotect(row, protection);
            var password = request.ClearPassword ? "" :
                string.IsNullOrEmpty(request.Password) ? previous?.Password ?? "" : request.Password;
            var configuration = new OutboundEmailConfiguration(request.Enabled, host, request.Port, fromAddress,
                fromName, dashboardUrl, username, password);
            if (request.Enabled && !configuration.IsValid())
                return Results.BadRequest(new { error = "Per attivare SMTP servono host, mittente valido, URL HTTPS, username e password." });

            row ??= new OutboundEmailSettings { Id = 1 };
            row.ProtectedConfiguration = protection.CreateProtector("OmniDesk.OutboundEmail.Configuration.v1")
                .Protect(JsonSerializer.Serialize(configuration, JsonOptions));
            if (db.Entry(row).State == EntityState.Detached) db.OutboundEmailSettings.Add(row);
            db.AuditEvents.Add(new AuditEvent { ActorId = context.User.UserId(), Action = "email.smtp.settings.update", Target = "1" });
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.NoContent();
        });

        admin.MapPost("/test", async (OutboundEmailTestRequest request, HttpContext context,
            IOutboundEmailSender sender, AppDbContext db, ILoggerFactory loggerFactory) =>
        {
            if (request.Recipient is not { Length: <= 320 } || !MailAddress.TryCreate(request.Recipient.Trim(), out var recipient))
                return Results.BadRequest(new { error = "Inserisci un indirizzo email valido per il test." });
            if (!await sender.IsEnabledAsync(context.RequestAborted))
                return Results.Conflict(new { error = "Salva una configurazione SMTP valida prima di inviare il test." });
            try
            {
                await sender.SendAsync(new OutboundEmailMessage(recipient.Address, "Test email OmniDesk",
                    "Questa è una email di verifica della configurazione SMTP di OmniDesk.",
                    $"smtp-test-{Guid.NewGuid():N}@omnidesk.invalid"), context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                loggerFactory.CreateLogger("OmniDesk.SmtpTest").LogWarning("SMTP test failed ({Type}).", exception.GetType().Name);
                db.AuditEvents.Add(new AuditEvent { ActorId = context.User.UserId(), Action = "email.smtp.test", Target = "failed", Outcome = "failed" });
                await db.SaveChangesAsync(context.RequestAborted);
                return Results.BadRequest(new { error = "Invio di test non riuscito. Verifica host, porta, credenziali e autorizzazioni del provider." });
            }
            db.AuditEvents.Add(new AuditEvent { ActorId = context.User.UserId(), Action = "email.smtp.test", Target = "sent" });
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.NoContent();
        });
    }

    private static OutboundEmailConfiguration Unprotect(OutboundEmailSettings row, IDataProtectionProvider protection)
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
}
