using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public sealed record BackupNasRequest(
    bool Enabled,
    string Server,
    string Share,
    string Username,
    string? Password,
    bool ClearPassword,
    string Domain,
    string AgeRecipient,
    string ScheduleTime,
    string TimeZoneId,
    int KeepCount);

public sealed record BackupNasConfiguration(
    bool Enabled,
    string Server,
    string Share,
    string Username,
    string Password,
    string Domain,
    string AgeRecipient,
    string ScheduleTime,
    string TimeZoneId,
    int KeepCount);

public static class BackupNasEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex HostPattern = new("^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$", RegexOptions.CultureInvariant);
    private static readonly Regex SharePattern = new("^[A-Za-z0-9._/-]{1,255}$", RegexOptions.CultureInvariant);
    private static readonly Regex TimePattern = new("^(?:[01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.CultureInvariant);
    private static readonly Regex RecipientPattern = new("^age1[0-9a-z]{20,90}$", RegexOptions.CultureInvariant);

    public static void MapBackupNasEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/backup-nas").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var permissions = ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>();
            return await permissions.Allows(ctx.HttpContext.User.UserId(), "administration")
                ? await next(ctx)
                : Results.Forbid();
        });

        admin.MapGet("", async (AppDbContext db, IDataProtectionProvider protection) =>
        {
            var row = await db.BackupNasSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
            var config = Unprotect(row, protection);
            return Results.Ok(new
            {
                configured = config is not null,
                enabled = config?.Enabled ?? false,
                server = config?.Server ?? "",
                share = config?.Share ?? "",
                username = config?.Username ?? "",
                passwordConfigured = !string.IsNullOrEmpty(config?.Password),
                domain = config?.Domain ?? "",
                ageRecipient = config?.AgeRecipient ?? "",
                scheduleTime = config?.ScheduleTime ?? "03:00",
                timeZoneId = config?.TimeZoneId ?? "Europe/Rome",
                keepCount = config?.KeepCount ?? 7,
                lastAttemptAt = row?.LastAttemptAt,
                lastSuccessAt = row?.LastSuccessAt,
                lastStatus = row?.LastStatus ?? "never",
                lastErrorCode = row?.LastErrorCode
            });
        });

        admin.MapPut("", async (BackupNasRequest request, HttpContext context, AppDbContext db, IDataProtectionProvider protection) =>
        {
            var server = request.Server?.Trim() ?? "";
            var share = request.Share?.Trim().Trim('/') ?? "";
            var username = request.Username?.Trim() ?? "";
            var domain = request.Domain?.Trim() ?? "";
            var recipient = request.AgeRecipient?.Trim() ?? "";
            var schedule = request.ScheduleTime?.Trim() ?? "";
            var timeZoneId = request.TimeZoneId?.Trim() ?? "";
            if (server.Length == 0 || !HostPattern.IsMatch(server) ||
                share.Length == 0 || !SharePattern.IsMatch(share) || share.Split('/').Any(x => x is "" or "." or "..") ||
                username.Length is 0 or > 128 || username.Contains('\n') || username.Contains('\r') || string.IsNullOrWhiteSpace(timeZoneId) ||
                domain.Length > 128 || domain.Contains('\n') || domain.Contains('\r') ||
                request.Password?.Length > 512 || request.Password?.Contains('\n') == true || request.Password?.Contains('\r') == true ||
                !RecipientPattern.IsMatch(recipient) || !TimePattern.IsMatch(schedule) ||
                request.KeepCount is < 1 or > 365)
                return Results.BadRequest(new { error = "Controlla server, share, utente, recipient age, orario e copie da conservare." });
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (TimeZoneNotFoundException) { return Results.BadRequest(new { error = "Fuso orario non riconosciuto." }); }
            catch (InvalidTimeZoneException) { return Results.BadRequest(new { error = "Fuso orario non valido." }); }

            var row = await db.BackupNasSettings.SingleOrDefaultAsync(x => x.Id == 1);
            var previous = Unprotect(row, protection);
            var password = request.ClearPassword ? "" : string.IsNullOrEmpty(request.Password) ? previous?.Password ?? "" : request.Password;
            if (request.Enabled && string.IsNullOrEmpty(password))
                return Results.BadRequest(new { error = "Inserisci la password Samba prima di attivare il backup." });

            row ??= new BackupNasSettings { Id = 1 };
            row.ProtectedConfiguration = protection.CreateProtector("OmniDesk.BackupNas.Configuration.v1")
                .Protect(JsonSerializer.Serialize(new BackupNasConfiguration(request.Enabled, server, share, username, password, domain, recipient, schedule, timeZoneId, request.KeepCount), JsonOptions));
            if (db.Entry(row).State == EntityState.Detached) db.BackupNasSettings.Add(row);
            db.AuditEvents.Add(new AuditEvent { ActorId = context.User.UserId(), Action = "backup.nas.settings.update", Target = "1" });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    public static BackupNasConfiguration? Unprotect(BackupNasSettings? row, IDataProtectionProvider protection)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.ProtectedConfiguration)) return null;
        try
        {
            var json = protection.CreateProtector("OmniDesk.BackupNas.Configuration.v1").Unprotect(row.ProtectedConfiguration);
            return JsonSerializer.Deserialize<BackupNasConfiguration>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Stored NAS backup settings cannot be decrypted.", exception);
        }
    }
}
