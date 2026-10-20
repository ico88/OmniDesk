using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Endpoints;

public sealed record NormalizedEmailPayload(string? MessageId, string? Mailbox, string? Sender,
    string? Subject, string? TextBody, DateTimeOffset ReceivedAt);
public sealed class SignedWebhookRequest { }

public static class EmailIntakeEndpoints
{
    private const int MaxBodyBytes = 64 * 1024;
    private const int MaxTextChars = 20_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapEmailIntakeEndpoints(this WebApplication app)
    {
        app.MapPost("/api/email/intake", Receive).WithMetadata(new SignedWebhookRequest());

        var admin = app.MapGroup("/api/admin/email-intakes").RequireAuthorization();
        admin.MapGet("", async (HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            if (!await permissions.Allows(c.User.UserId(), "tickets.manage")) return Results.Forbid();
            var items = await db.SupportEmailIntakes.AsNoTracking()
                .Where(x => x.State == "pending")
                .OrderBy(x => x.ReceivedAt).Take(100)
                .Select(x => new { x.Id, x.MailboxAddress, x.MessageId, x.SenderAddress, x.Subject,
                    x.BodyText, x.MessageReceivedAt, x.ReceivedAt, x.State })
                .ToArrayAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(items);
        });
        admin.MapPost("/{id:guid}/discard", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            if (!await permissions.Allows(c.User.UserId(), "tickets.manage")) return Results.Forbid();
            var row = await db.SupportEmailIntakes.SingleOrDefaultAsync(x => x.Id == id && x.State == "pending");
            if (row is null) return Results.NotFound();
            row.State = "discarded";
            row.QualifiedAt = DateTimeOffset.UtcNow;
            row.QualifiedBy = c.User.UserId();
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.email.intake.discarded", Target = row.Id.ToString() });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<IResult> Receive(HttpContext c, AppDbContext db, IConfiguration configuration)
    {
        var path = configuration["EmailIntake:SigningKeyFile"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        byte[] secret;
        try { secret = await File.ReadAllBytesAsync(path, c.RequestAborted); }
        catch (IOException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        if (secret.Length < 32) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (!string.Equals(c.Request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        if (c.Request.ContentLength is > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await c.Request.Body.ReadAsync(chunk, c.RequestAborted);
            if (read == 0) break;
            if (buffer.Length + read > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }
        var body = buffer.ToArray();
        var sentAt = c.Request.Headers["X-OmniDesk-Mail-Timestamp"].ToString();
        var signature = c.Request.Headers["X-OmniDesk-Mail-Signature"].ToString();
        if (!EmailIntakeSignature.Verify(secret, sentAt, signature, body, DateTimeOffset.UtcNow, out _))
            return Results.Unauthorized();

        NormalizedEmailPayload? payload;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != document.RootElement.EnumerateObject().Count())
                return Results.BadRequest(new { error = "Payload email non valido." });
            payload = document.RootElement.Deserialize<NormalizedEmailPayload>(JsonOptions);
        }
        catch (JsonException) { return Results.BadRequest(new { error = "Payload email non valido." }); }

        var mailbox = NormalizeAddress(payload?.Mailbox);
        var sender = NormalizeAddress(payload?.Sender);
        var allowed = configuration.GetSection("EmailIntake:AllowedMailboxes").Get<string[]>() ?? [];
        var allowlisted = allowed.Select(NormalizeAddress).Where(x => x is not null).Cast<string>()
            .Contains(mailbox, StringComparer.OrdinalIgnoreCase);
        if (!allowlisted || mailbox is null || sender is null || payload is null ||
            string.IsNullOrWhiteSpace(payload.MessageId) || payload.MessageId.Length > 512 ||
            payload.MessageId.Any(char.IsControl) || string.IsNullOrWhiteSpace(payload.Subject) || payload.Subject.Trim().Length > 200 ||
            payload.Subject.Any(char.IsControl) || payload.TextBody is null || payload.TextBody.Length > MaxTextChars ||
            payload.ReceivedAt > DateTimeOffset.UtcNow.AddMinutes(10))
            return Results.BadRequest(new { error = "Payload email non valido o mailbox non autorizzata." });

        var messageId = payload.MessageId.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var existing = await db.SupportEmailIntakes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MailboxAddress == mailbox && x.MessageId == messageId, c.RequestAborted);
        if (existing is not null) return DuplicateResult(existing, hash);

        var intake = new SupportEmailIntake
        {
            MailboxAddress = mailbox,
            MessageId = messageId,
            SenderAddress = sender,
            Subject = payload.Subject.Trim(),
            BodyText = payload.TextBody,
            PayloadSha256 = hash,
            MessageReceivedAt = payload.ReceivedAt.ToUniversalTime(),
            ReceivedAt = DateTimeOffset.UtcNow
        };
        db.SupportEmailIntakes.Add(intake);
        db.AuditEvents.Add(new AuditEvent { Action = "ticket.email.intake.received", Target = intake.Id.ToString() });
        try { await db.SaveChangesAsync(c.RequestAborted); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.SupportEmailIntakes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.MailboxAddress == mailbox && x.MessageId == messageId, c.RequestAborted);
            if (existing is null) throw;
            return DuplicateResult(existing, hash);
        }

        return Results.Accepted($"/api/admin/email-intakes/{intake.Id}", new { id = intake.Id, state = intake.State, duplicate = false });
    }

    private static IResult DuplicateResult(SupportEmailIntake existing, string hash)
    {
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.PayloadSha256), Convert.FromHexString(hash)))
            return Results.Conflict(new { error = "Message-ID già acquisito con contenuto diverso." });
        return Results.Ok(new { id = existing.Id, state = existing.State, duplicate = true });
    }

    private static string? NormalizeAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320 || value.Any(char.IsControl)) return null;
        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
                ? address.Address.ToLowerInvariant() : null;
        }
        catch (FormatException) { return null; }
    }
}
