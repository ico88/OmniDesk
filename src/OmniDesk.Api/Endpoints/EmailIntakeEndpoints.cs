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
public sealed record EmailIntakePromotion(Guid DeviceId, string Subject, string Description, string Category, string Priority,
    bool ConfirmUnverifiedSender);
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
            await using var tx = await db.Database.BeginTransactionAsync();
            var now = DateTimeOffset.UtcNow;
            var changed = await db.SupportEmailIntakes.Where(x => x.Id == id && x.State == "pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "discarded")
                    .SetProperty(x => x.QualifiedAt, now).SetProperty(x => x.QualifiedBy, c.User.UserId()));
            if (changed == 0) return await db.SupportEmailIntakes.AnyAsync(x => x.Id == id) ? Results.Conflict() : Results.NotFound();
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.email.intake.discarded", Target = id.ToString() });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        admin.MapPost("/{id:guid}/promote", async (Guid id, EmailIntakePromotion request, HttpContext c,
            AppDbContext db, PermissionService permissions) =>
        {
            var subject = request.Subject?.Trim();
            var description = request.Description?.Trim();
            var category = request.Category?.Trim();
            var priority = request.Priority?.Trim().ToLowerInvariant();
            if (!request.ConfirmUnverifiedSender || request.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(subject) || subject.Length > 200 ||
                string.IsNullOrWhiteSpace(description) || description.Length > 8000 ||
                !SupportEndpoints.ValidCategory(category) || priority is not ("p1" or "p2" or "p3" or "p4"))
                return Results.BadRequest(new { error = "Dati ticket non validi." });

            var userId = c.User.UserId();
            if (!await permissions.Allows(userId, "tickets.manage", request.DeviceId)) return Results.Forbid();
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.DeviceId);
            if (device is null) return Results.NotFound();
            if (!await db.Sites.AnyAsync(x => x.Id == device.SiteId && !x.IsArchived))
                return Results.Conflict(new { error = "Il dispositivo non è associato a una sede attiva." });

            var alreadyPromoted = await (from existingIntake in db.SupportEmailIntakes.AsNoTracking()
                join existingTicket in db.SupportTickets.AsNoTracking() on existingIntake.PromotedTicketId equals existingTicket.Id
                where existingIntake.Id == id && existingIntake.State == "promoted"
                select new { ticketId = existingTicket.Id, existingTicket.Number, existingTicket.DeviceId }).SingleOrDefaultAsync();
            if (alreadyPromoted is not null)
                return alreadyPromoted.DeviceId == request.DeviceId
                    ? Results.Ok(new { ticketId = alreadyPromoted.ticketId, number = alreadyPromoted.Number, duplicate = true })
                    : Results.Conflict(new { error = "Il messaggio è già stato convertito in un altro ticket." });

            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, request.DeviceId);
            var claimed = await db.SupportEmailIntakes.Where(x => x.Id == id && x.State == "pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "promoting"), c.RequestAborted);
            if (claimed == 0) return Results.Conflict(new { error = "Il messaggio non è più in attesa. Aggiorna la coda." });

            var intake = await db.SupportEmailIntakes.AsNoTracking().SingleAsync(x => x.Id == id, c.RequestAborted);
            var queueId = await db.SupportQueueRoutingRules.AsNoTracking()
                .Where(rule => rule.Category == category && (rule.SiteId == device.SiteId || rule.SiteId == null) &&
                    db.SupportQueues.Any(queue => queue.Id == rule.QueueId && queue.IsActive && (queue.SiteId == null || queue.SiteId == device.SiteId)))
                .OrderByDescending(rule => rule.SiteId != null).Select(rule => (Guid?)rule.QueueId).FirstOrDefaultAsync(c.RequestAborted);
            var workflowBinding = await TicketWorkflowResolver.BindForNewTicket(db, device.SiteId);
            var ticket = new SupportTicket
            {
                DeviceId = device.Id,
                SiteId = device.SiteId,
                RequestId = Guid.NewGuid(),
                RequesterKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                RequesterUserId = null,
                Origin = "email",
                Subject = subject,
                Description = description,
                Category = category!,
                Priority = priority!,
                QueueId = queueId,
                WorkflowId = workflowBinding.WorkflowId,
                WorkflowVersion = workflowBinding.WorkflowVersion
            };
            try { await TicketSlaTargetService.ApplyInitialTargets(db, ticket, c.RequestAborted); }
            catch (SlaPolicyConfigurationException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }

            db.SupportTickets.Add(ticket);
            db.SupportTicketDevices.Add(new SupportTicketDevice
            {
                TicketId = ticket.Id,
                DeviceId = device.Id,
                DeviceNameSnapshot = device.Name,
                SiteIdSnapshot = device.SiteId,
                SiteNameSnapshot = await db.Sites.Where(x => x.Id == device.SiteId).Select(x => x.Name).SingleAsync(c.RequestAborted),
                LinkedBy = userId
            });
            CoreOutbox.Add(db, "ticket.created", "ticket", ticket.Id,
                new { ticketId = ticket.Id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, state = ticket.State, category = ticket.Category, queueId = ticket.QueueId, origin = ticket.Origin }, c.TraceIdentifier);
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.email.intake.promoted", Target = intake.Id.ToString() });
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.manual.create", Target = ticket.Id.ToString() });
            await db.SaveChangesAsync(c.RequestAborted);
            var now = DateTimeOffset.UtcNow;
            var completed = await db.SupportEmailIntakes.Where(x => x.Id == id && x.State == "promoting")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "promoted")
                    .SetProperty(x => x.QualifiedAt, now).SetProperty(x => x.QualifiedBy, userId)
                    .SetProperty(x => x.PromotedTicketId, ticket.Id), c.RequestAborted);
            if (completed != 1) throw new InvalidOperationException("Email intake promotion lost its transactional claim.");
            await tx.CommitAsync(c.RequestAborted);
            return Results.Created($"/api/support/{ticket.Id}", new { ticketId = ticket.Id, number = ticket.Number, duplicate = false });
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
