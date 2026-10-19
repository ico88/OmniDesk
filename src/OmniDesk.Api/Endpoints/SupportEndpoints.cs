using System.IO.Compression;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;

namespace OmniDesk.Api.Endpoints;

public sealed record SupportStaffEdit(long ExpectedVersion,string State,string? Resolution,bool AddReply=false,
    string? Priority=null,bool UpdatePriority=false,string? AssigneeId=null,bool UpdateAssignment=false,
    Guid? QueueId=null,bool UpdateQueue=false);
public sealed record SupportInternalNote(long ExpectedVersion, string Body);
public sealed record ManualSupportCreate(Guid RequestId, Guid DeviceId, string Subject, string Description, string Category, string Priority);
public sealed record SupportTicketDeviceChange(long ExpectedVersion, Guid DeviceId);
public sealed record SupportTicketDeviceView(Guid DeviceId, string Name, Guid SiteId, string SiteName, bool IsPrimary);
public sealed record SupportTicketVersion(Guid TicketId, long ExpectedVersion);
public sealed record BulkQueueAssignment(SupportTicketVersion[] Tickets, Guid? QueueId);
public sealed record PortalTicketCreate(Guid RequestId, Guid DeviceId, string Subject, string Description, string Category);
public sealed record PortalTicketReply(long ExpectedVersion, string Body);
public sealed record PortalTicketView(Guid Id, long Number, Guid DeviceId, string DeviceName, string SiteName,
    string Subject, string State, string Category, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PortalMessageView(long Id, string Kind, string Body, string? FromState, string? ToState, DateTimeOffset CreatedAt);
public static class SupportEndpoints
{
    private static readonly string[] Priorities = ["low", "normal", "high", "urgent"];
    private static readonly string[] Categories = ["general", "hardware", "software", "network", "account", "other"];
    public static bool ValidCategory(string? category) => category is not null && Categories.Contains(category);
    private static bool ValidKey(string? key) => key is { Length: 64 } && key.All(Uri.IsHexDigit);
    private static SupportTicketView View(SupportTicket t) => new(t.Id, t.Number, t.DeviceId, t.Subject, t.Description, t.State,
        t.Resolution, t.Version, t.CreatedAt, t.UpdatedAt, t.Screenshot != null, t.Category, t.Origin);
    public static void MapSupportEndpoints(this WebApplication app)
    {
        var machine = app.MapGroup("/api/agent/support").WithMetadata(new MachineRequest())
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = DeviceAuthentication.SchemeName });
        machine.MapGet("/{requesterKey}", async (string requesterKey, HttpContext c, AppDbContext db) => {
            if (!ValidKey(requesterKey)) return Results.BadRequest();
            var deviceId = Guid.Parse(c.User.UserId());
            var rows = await db.SupportTickets.AsNoTracking().Where(t => t.DeviceId == deviceId && t.RequesterKey == requesterKey)
                .OrderByDescending(t => t.CreatedAt).Take(10)
                .Select(t => new SupportTicketView(t.Id,t.Number,t.DeviceId,t.Subject,"",t.State,t.Resolution,t.Version,t.CreatedAt,t.UpdatedAt,t.Screenshot != null,t.Category)).ToArrayAsync();
            return Results.Ok(rows);
        });
        machine.MapGet("/{requesterKey}/{ticketId:guid}/messages", async (string requesterKey, Guid ticketId, HttpContext c, AppDbContext db) => {
            if (!ValidKey(requesterKey)) return Results.BadRequest();
            var deviceId = Guid.Parse(c.User.UserId());
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId && t.DeviceId == deviceId && t.RequesterKey == requesterKey);
            if (ticket is null) return Results.NotFound();
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == deviceId && x.RevokedAt == null)) return Results.Unauthorized();
            var replies = await db.SupportMessages.AsNoTracking().Where(m => m.TicketId == ticketId && m.Visibility == "public")
                .OrderBy(m => m.Id).Select(m => new SupportRequesterMessageView(m.Id, m.Kind, m.Body, m.FromState, m.ToState, m.CreatedAt)).ToArrayAsync();
            var messages = new[] { new SupportRequesterMessageView(0, "request", ticket.Description, null, null, ticket.CreatedAt) }
                .Concat(replies).ToArray();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new SupportRequesterConversation(ticket.Id, ticket.Version, ticket.State, messages));
        });
        machine.MapPost("/{requesterKey}/{ticketId:guid}/messages", async (string requesterKey, Guid ticketId, SupportRequesterReply r, HttpContext c, AppDbContext db) => {
            if (!ValidKey(requesterKey) || r.RequesterKey != requesterKey || r.ExpectedVersion < 1 ||
                string.IsNullOrWhiteSpace(r.Body) || r.Body.Trim().Length > 8000) return Results.BadRequest();
            var deviceId = Guid.Parse(c.User.UserId());
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, deviceId);
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == deviceId && x.RevokedAt == null)) return Results.Unauthorized();
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId && t.DeviceId == deviceId && t.RequesterKey == requesterKey);
            if (ticket is null) return Results.NotFound();
            if (ticket.Version != r.ExpectedVersion) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica la conversazione." });
            if (ticket.State is "resolved" or "closed") return Results.Conflict(new { error = "Il ticket è chiuso. Apri una nuova richiesta per ricevere assistenza." });
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
            if (await db.SupportMessages.CountAsync(m => m.TicketId == ticketId && m.Kind == "requester_reply" && m.CreatedAt > cutoff) >= 10)
                return Results.StatusCode(429);
            var now = DateTimeOffset.UtcNow;
            var changed = await db.SupportTickets.Where(t => t.Id == ticketId && t.DeviceId == deviceId && t.RequesterKey == requesterKey &&
                    t.Version == r.ExpectedVersion && t.State != "resolved" && t.State != "closed")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedAt, now).SetProperty(t => t.Version, t => t.Version + 1));
            if (changed == 0) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica la conversazione." });
            db.SupportMessages.Add(new SupportMessage { TicketId = ticketId, Kind = "requester_reply", Body = r.Body.Trim(), CreatedAt = now });
            db.AuditEvents.Add(new AuditEvent { Action = "ticket.requester.reply", Target = ticketId.ToString() });
            CoreOutbox.Add(db, "ticket.requester_replied", "ticket", ticketId,
                new { ticketId, deviceId, siteId = ticket.SiteId, queueId = ticket.QueueId, assigneeId = ticket.AssigneeId, state = ticket.State, version = r.ExpectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        machine.MapPost("", async (SupportIntake r, HttpContext c, AppDbContext db) => {
            if (!ValidKey(r.RequesterKey) || r.Ticket is null || r.Ticket.RequestId == Guid.Empty ||
                string.IsNullOrWhiteSpace(r.Ticket.Subject) || r.Ticket.Subject.Length > 200 ||
                string.IsNullOrWhiteSpace(r.Ticket.Description) || r.Ticket.Description.Length > 8000 || !Categories.Contains(r.Ticket.Category)) return Results.BadRequest();
            byte[]? screenshot = null;
            if (r.Ticket.ScreenshotBase64 is not null)
            {
                if (!r.Ticket.ScreenshotConsent || r.Ticket.ScreenshotBase64.Length > 700000) return Results.BadRequest();
                try { screenshot = Convert.FromBase64String(r.Ticket.ScreenshotBase64); }
                catch (FormatException) { return Results.BadRequest(); }
                if (!ValidScreenshot(screenshot)) return Results.BadRequest();
            }
            var id = Guid.Parse(c.User.UserId());
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return Results.Unauthorized();
            var siteId = await db.Devices.Where(x => x.Id == id).Select(x => x.SiteId).SingleOrDefaultAsync();
            if (siteId == Guid.Empty) return Results.Conflict();
            var previous = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.DeviceId == id && t.RequesterKey == r.RequesterKey && t.RequestId == r.Ticket.RequestId);
            if (previous != null)
            {
                if (previous.Subject != r.Ticket.Subject.Trim() || previous.Description != r.Ticket.Description.Trim() ||
                    previous.Category != r.Ticket.Category || !(previous.Screenshot ?? []).SequenceEqual(screenshot ?? [])) return Results.Conflict();
                return Results.Ok(View(previous));
            }
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
            if (await db.SupportTickets.CountAsync(t => t.DeviceId == id && t.CreatedAt > cutoff) >= 10)
                return Results.StatusCode(429);
            var queueId = await db.SupportQueueRoutingRules.AsNoTracking()
                .Where(rule => rule.Category == r.Ticket.Category && (rule.SiteId == siteId || rule.SiteId == null) &&
                    db.SupportQueues.Any(queue => queue.Id == rule.QueueId && queue.IsActive && (queue.SiteId == null || queue.SiteId == siteId)))
                .OrderByDescending(rule => rule.SiteId != null).Select(rule => (Guid?)rule.QueueId).FirstOrDefaultAsync();
            var ticket = new SupportTicket { DeviceId = id, SiteId = siteId, RequesterKey = r.RequesterKey, RequestId = r.Ticket.RequestId,
                Subject = r.Ticket.Subject.Trim(), Description = r.Ticket.Description.Trim(), Category = r.Ticket.Category, QueueId = queueId, Screenshot = screenshot };
            db.SupportTickets.Add(ticket);
            var primaryDeviceName = await db.Devices.Where(x => x.Id == id).Select(x => x.Name).SingleAsync();
            var primarySiteName = await db.Sites.Where(x => x.Id == siteId).Select(x => x.Name).SingleAsync();
            db.SupportTicketDevices.Add(new SupportTicketDevice { TicketId = ticket.Id, DeviceId = id,
                DeviceNameSnapshot = primaryDeviceName, SiteIdSnapshot = siteId, SiteNameSnapshot = primarySiteName });
            CoreOutbox.Add(db, "ticket.created", "ticket", ticket.Id,
                new { ticketId = ticket.Id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, state = ticket.State, category = ticket.Category, queueId = ticket.QueueId }, c.TraceIdentifier);
            db.AuditEvents.Add(new AuditEvent { Action = "ticket.create", Target = ticket.Id.ToString() });
            if (screenshot != null) db.AuditEvents.Add(new AuditEvent { Action = "ticket.screenshot.consent", Target = ticket.Id.ToString() });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/support/{ticket.Id}", View(ticket));
        });
        var portal = app.MapGroup("/api/portal").RequireAuthorization();
        portal.MapGet("/devices", async (HttpContext c, AppDbContext db) => {
            var userId = c.User.UserId();
            if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDisabled)) return Results.Forbid();
            var devices = await (from access in db.PortalDeviceAccesses.AsNoTracking()
                join device in db.Devices.AsNoTracking() on access.DeviceId equals device.Id
                join site in db.Sites.AsNoTracking() on device.SiteId equals site.Id
                where access.UserId == userId && !site.IsArchived &&
                    db.DeviceCredentials.Any(credential => credential.DeviceId == device.Id && credential.RevokedAt == null)
                orderby site.Name, device.Name
                select new { deviceId = device.Id, deviceName = device.Name, siteId = site.Id, siteName = site.Name })
                .ToArrayAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(devices);
        });
        portal.MapGet("/tickets", async (HttpContext c, AppDbContext db) => {
            var userId = c.User.UserId();
            if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDisabled)) return Results.Forbid();
            var tickets = await (from ticket in db.SupportTickets.AsNoTracking()
                join device in db.Devices.AsNoTracking() on ticket.DeviceId equals device.Id
                join site in db.Sites.AsNoTracking() on ticket.SiteId equals site.Id
                where ticket.RequesterUserId == userId && !site.IsArchived &&
                    db.PortalDeviceAccesses.Any(access => access.UserId == userId && access.DeviceId == ticket.DeviceId) &&
                    db.DeviceCredentials.Any(credential => credential.DeviceId == ticket.DeviceId && credential.RevokedAt == null)
                orderby ticket.UpdatedAt descending
                select new PortalTicketView(ticket.Id, ticket.Number, ticket.DeviceId, device.Name, site.Name,
                    ticket.Subject, ticket.State, ticket.Category, ticket.Version, ticket.CreatedAt, ticket.UpdatedAt))
                .Take(100).ToArrayAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(tickets);
        });
        portal.MapGet("/tickets/{id:guid}", async (Guid id, HttpContext c, AppDbContext db) => {
            var userId = c.User.UserId();
            var ticket = await (from item in db.SupportTickets.AsNoTracking()
                join device in db.Devices.AsNoTracking() on item.DeviceId equals device.Id
                join site in db.Sites.AsNoTracking() on item.SiteId equals site.Id
                where item.Id == id && item.RequesterUserId == userId && !site.IsArchived &&
                    db.Users.Any(user => user.Id == userId && !user.IsDisabled) &&
                    db.PortalDeviceAccesses.Any(access => access.UserId == userId && access.DeviceId == item.DeviceId) &&
                    db.DeviceCredentials.Any(credential => credential.DeviceId == item.DeviceId && credential.RevokedAt == null)
                select new PortalTicketView(item.Id, item.Number, item.DeviceId, device.Name, site.Name,
                    item.Subject, item.State, item.Category, item.Version, item.CreatedAt, item.UpdatedAt))
                .SingleOrDefaultAsync();
            if (ticket is null) return Results.NotFound();
            var request = await db.SupportTickets.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Description, x.CreatedAt }).SingleAsync();
            var replies = await db.SupportMessages.AsNoTracking().Where(message => message.TicketId == id && message.Visibility == "public")
                .OrderBy(message => message.Id)
                .Select(message => new PortalMessageView(message.Id, message.Kind, message.Body, message.FromState, message.ToState, message.CreatedAt))
                .ToArrayAsync();
            var messages = new[] { new PortalMessageView(0, "request", request.Description, null, null, request.CreatedAt) }
                .Concat(replies).ToArray();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { ticket, messages });
        });
        portal.MapPost("/tickets", async (PortalTicketCreate r, HttpContext c, AppDbContext db) => {
            var subject = r.Subject?.Trim();
            var description = r.Description?.Trim();
            var category = r.Category ?? "";
            if (r.RequestId == Guid.Empty || r.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(subject) || subject.Length > 200 ||
                string.IsNullOrWhiteSpace(description) || description.Length > 8000 || !Categories.Contains(category)) return Results.BadRequest();
            var userId = c.User.UserId();
            if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDisabled)) return Results.Forbid();
            await using var tx = await db.Database.BeginTransactionAsync();
            await PortalDeviceAccessLock.Acquire(db, userId, r.DeviceId);
            var device = await (from item in db.Devices.AsNoTracking()
                join site in db.Sites.AsNoTracking() on item.SiteId equals site.Id
                where item.Id == r.DeviceId && !site.IsArchived &&
                    db.PortalDeviceAccesses.Any(access => access.UserId == userId && access.DeviceId == item.Id) &&
                    db.DeviceCredentials.Any(credential => credential.DeviceId == item.Id && credential.RevokedAt == null)
                select new { item.Id, item.Name, item.SiteId, SiteName = site.Name }).SingleOrDefaultAsync();
            if (device is null) return Results.Forbid();
            var requesterKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"portal:{userId}:{r.RequestId:D}"))).ToLowerInvariant();
            var previous = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(ticket => ticket.DeviceId == device.Id &&
                ticket.RequesterKey == requesterKey && ticket.RequestId == r.RequestId && ticket.RequesterUserId == userId);
            if (previous is not null)
                return previous.Subject == subject && previous.Description == description && previous.Category == r.Category
                    ? Results.Ok(new PortalTicketView(previous.Id, previous.Number, device.Id, device.Name, device.SiteName,
                        previous.Subject, previous.State, previous.Category, previous.Version, previous.CreatedAt, previous.UpdatedAt))
                    : Results.Conflict();
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
            if (await db.SupportTickets.CountAsync(ticket => ticket.RequesterUserId == userId && ticket.CreatedAt > cutoff) >= 10)
                return Results.StatusCode(429);
            var queueId = await db.SupportQueueRoutingRules.AsNoTracking()
                .Where(rule => rule.Category == category && (rule.SiteId == device.SiteId || rule.SiteId == null) &&
                    db.SupportQueues.Any(queue => queue.Id == rule.QueueId && queue.IsActive && (queue.SiteId == null || queue.SiteId == device.SiteId)))
                .OrderByDescending(rule => rule.SiteId != null).Select(rule => (Guid?)rule.QueueId).FirstOrDefaultAsync();
            var now = DateTimeOffset.UtcNow;
            var ticket = new SupportTicket {
                DeviceId = device.Id, SiteId = device.SiteId, RequesterUserId = userId, RequesterKey = requesterKey,
                RequestId = r.RequestId, Origin = "portal", Subject = subject, Description = description,
                Category = category, Priority = "normal", QueueId = queueId, CreatedAt = now, UpdatedAt = now
            };
            db.SupportTickets.Add(ticket);
            db.SupportTicketDevices.Add(new SupportTicketDevice { TicketId = ticket.Id, DeviceId = device.Id,
                LinkedAt = now, DeviceNameSnapshot = device.Name, SiteIdSnapshot = device.SiteId, SiteNameSnapshot = device.SiteName });
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.portal.create", Target = ticket.Id.ToString() });
            CoreOutbox.Add(db, "ticket.created", "ticket", ticket.Id,
                new { ticketId = ticket.Id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, state = ticket.State,
                    category = ticket.Category, queueId = ticket.QueueId, origin = ticket.Origin }, c.TraceIdentifier);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/portal/tickets/{ticket.Id}", new PortalTicketView(ticket.Id, ticket.Number,
                device.Id, device.Name, device.SiteName, ticket.Subject, ticket.State, category,
                ticket.Version, ticket.CreatedAt, ticket.UpdatedAt));
        });
        portal.MapPost("/tickets/{id:guid}/messages", async (Guid id, PortalTicketReply r, HttpContext c, AppDbContext db) => {
            if (r.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(r.Body) || r.Body.Trim().Length > 8000) return Results.BadRequest();
            var userId = c.User.UserId();
            var deviceId = await db.SupportTickets.AsNoTracking().Where(ticket => ticket.Id == id && ticket.RequesterUserId == userId &&
                db.PortalDeviceAccesses.Any(access => access.UserId == userId && access.DeviceId == ticket.DeviceId))
                .Select(ticket => (Guid?)ticket.DeviceId).SingleOrDefaultAsync();
            if (deviceId is null) return Results.NotFound();
            await using var tx = await db.Database.BeginTransactionAsync();
            await PortalDeviceAccessLock.Acquire(db, userId, deviceId.Value);
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.RequesterUserId == userId &&
                db.Users.Any(user => user.Id == userId && !user.IsDisabled) &&
                db.PortalDeviceAccesses.Any(access => access.UserId == userId && access.DeviceId == item.DeviceId) &&
                db.DeviceCredentials.Any(credential => credential.DeviceId == item.DeviceId && credential.RevokedAt == null));
            if (ticket is null) return Results.NotFound();
            if (ticket.Version != r.ExpectedVersion) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica la conversazione." });
            if (ticket.State is "resolved" or "closed") return Results.Conflict(new { error = "Il ticket è chiuso. Apri una nuova richiesta per ricevere assistenza." });
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
            if (await db.SupportMessages.CountAsync(message => message.TicketId == id && message.Kind == "portal_reply" && message.CreatedAt > cutoff) >= 10)
                return Results.StatusCode(429);
            var now = DateTimeOffset.UtcNow;
            var changed = await db.SupportTickets.Where(item => item.Id == id && item.RequesterUserId == userId &&
                    item.Version == r.ExpectedVersion && item.State != "resolved" && item.State != "closed" &&
                    db.PortalDeviceAccesses.Any(access => access.UserId == userId && access.DeviceId == item.DeviceId))
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.UpdatedAt, now).SetProperty(item => item.Version, item => item.Version + 1));
            if (changed == 0) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica la conversazione." });
            db.SupportMessages.Add(new SupportMessage { TicketId = id, ActorId = userId, Kind = "portal_reply", Visibility = "public", Body = r.Body.Trim(), CreatedAt = now });
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.portal.reply", Target = id.ToString() });
            CoreOutbox.Add(db, "ticket.requester_replied", "ticket", id,
                new { ticketId = id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, queueId = ticket.QueueId,
                    assigneeId = ticket.AssigneeId, state = ticket.State, version = r.ExpectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        var staff = app.MapGroup("/api/support").RequireAuthorization();
        staff.MapPost("/manual", async (ManualSupportCreate r, HttpContext c, AppDbContext db, PermissionService p) => {
            if (r.RequestId == Guid.Empty || r.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(r.Subject) || r.Subject.Trim().Length > 200 ||
                string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length > 8000 || !ValidCategory(r.Category) || !Priorities.Contains(r.Priority)) return Results.BadRequest();
            var userId = c.User.UserId();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, r.DeviceId);
            if (!await p.Allows(userId, "tickets.manage", r.DeviceId)) return Results.Forbid();
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.DeviceId);
            if (device is null) return Results.NotFound();
            var requesterKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("operator:" + userId))).ToLowerInvariant();
            var existing = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.DeviceId == r.DeviceId && t.RequesterKey == requesterKey && t.RequestId == r.RequestId);
            if (existing is not null)
                return existing.Origin == "operator" && existing.Subject == r.Subject.Trim() && existing.Description == r.Description.Trim() && existing.Category == r.Category && existing.Priority == r.Priority
                    ? Results.Ok(View(existing)) : Results.Conflict();
            var queueId = await db.SupportQueueRoutingRules.AsNoTracking()
                .Where(rule => rule.Category == r.Category && (rule.SiteId == device.SiteId || rule.SiteId == null) &&
                    db.SupportQueues.Any(queue => queue.Id == rule.QueueId && queue.IsActive && (queue.SiteId == null || queue.SiteId == device.SiteId)))
                .OrderByDescending(rule => rule.SiteId != null).Select(rule => (Guid?)rule.QueueId).FirstOrDefaultAsync();
            var ticket = new SupportTicket { DeviceId = device.Id, SiteId = device.SiteId, RequesterKey = requesterKey, RequestId = r.RequestId,
                Origin = "operator", Subject = r.Subject.Trim(), Description = r.Description.Trim(), Category = r.Category, Priority = r.Priority, QueueId = queueId };
            db.SupportTickets.Add(ticket);
            var deviceName = await db.Devices.Where(x => x.Id == device.Id).Select(x => x.Name).SingleAsync();
            var siteName = await db.Sites.Where(x => x.Id == device.SiteId).Select(x => x.Name).SingleAsync();
            db.SupportTicketDevices.Add(new SupportTicketDevice { TicketId = ticket.Id, DeviceId = device.Id,
                DeviceNameSnapshot = deviceName, SiteIdSnapshot = device.SiteId, SiteNameSnapshot = siteName, LinkedBy = userId });
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.manual.create", Target = ticket.Id.ToString() });
            CoreOutbox.Add(db, "ticket.created", "ticket", ticket.Id,
                new { ticketId = ticket.Id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, state = ticket.State, category = ticket.Category, queueId = ticket.QueueId, origin = ticket.Origin }, c.TraceIdentifier);
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/support/{ticket.Id}", View(ticket));
        });
        staff.MapGet("/{id:guid}/devices", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            var userId = c.User.UserId();
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(userId, "tickets.view", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            var global = (await p.Global(userId)).Contains("tickets.view");
            var grants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                where g.UserId == userId && permission.Permission == "tickets.view" select g;
            var visible = await (from link in db.SupportTicketDevices.AsNoTracking()
                join device in db.Devices.AsNoTracking() on link.DeviceId equals device.Id
                join site in db.Sites.AsNoTracking() on device.SiteId equals site.Id
                where link.TicketId == id && db.Users.Any(u => u.Id == userId && !u.IsDisabled) &&
                    (global || grants.Any(g => g.DeviceId == device.Id || g.SiteId == device.SiteId ||
                        (device.GroupId != null && g.GroupId == device.GroupId)))
                orderby link.LinkedAt, device.Name
                select new SupportTicketDeviceView(device.Id, link.DeviceNameSnapshot, link.SiteIdSnapshot,
                    link.SiteNameSnapshot, device.Id == ticket.DeviceId)).ToArrayAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(visible);
        });
        staff.MapGet("/{id:guid}/linkable-devices", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            var userId = c.User.UserId();
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(userId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            var global = (await p.Global(userId)).Contains("tickets.manage");
            var grants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                where g.UserId == userId && permission.Permission == "tickets.manage" select g;
            var rows = await db.Devices.AsNoTracking().Where(d => d.SiteId == ticket.SiteId && db.Users.Any(u => u.Id == userId && !u.IsDisabled) &&
                    (global || grants.Any(g => g.DeviceId == d.Id || g.SiteId == d.SiteId ||
                        (d.GroupId != null && g.GroupId == d.GroupId))) &&
                    !db.SupportTicketDevices.Any(link => link.TicketId == id && link.DeviceId == d.Id))
                .OrderBy(d => d.Name).Take(200)
                .Select(d => new { deviceId = d.Id, d.Name, d.SiteId, siteName = db.Sites.Where(s => s.Id == d.SiteId).Select(s => s.Name).FirstOrDefault() })
                .ToArrayAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(rows);
        });
        staff.MapPost("/{id:guid}/devices", async (Guid id, SupportTicketDeviceChange r, HttpContext c, AppDbContext db, PermissionService p) => {
            if (r.ExpectedVersion < 1 || r.DeviceId == Guid.Empty) return Results.BadRequest();
            var userId = c.User.UserId();
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(userId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            if (r.DeviceId == ticket.DeviceId) return Results.BadRequest(new { error = "Il dispositivo principale è già collegato al ticket." });
            var target = await (from d in db.Devices.AsNoTracking() join site in db.Sites.AsNoTracking() on d.SiteId equals site.Id
                where d.Id == r.DeviceId select new { d.Id, d.Name, d.SiteId, SiteName = site.Name }).SingleOrDefaultAsync();
            if (target is null) return Results.NotFound();
            if (!await p.Allows(userId, "tickets.manage", target.Id)) return Results.Forbid();
            if (target.SiteId != ticket.SiteId) return Results.BadRequest(new { error = "Tutti i dispositivi del ticket devono appartenere alla stessa sede." });
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var lockId in new[] { ticket.DeviceId, target.Id }.Distinct().Order())
                await DeviceOperationLock.Acquire(db, lockId);
            var currentTarget = await (from d in db.Devices.AsNoTracking() join site in db.Sites.AsNoTracking() on d.SiteId equals site.Id
                where d.Id == target.Id select new { d.Id, d.Name, d.SiteId, SiteName = site.Name }).SingleOrDefaultAsync();
            if (currentTarget is null) return Results.NotFound();
            if (currentTarget.SiteId != ticket.SiteId) return Results.Conflict(new { error = "La sede del dispositivo è cambiata. Ricarica il ticket." });
            if (!await p.Allows(userId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId) ||
                !await p.Allows(userId, "tickets.manage", currentTarget.Id)) return Results.Forbid();
            if (await db.SupportTicketDevices.AnyAsync(x => x.TicketId == id && x.DeviceId == currentTarget.Id))
                return Results.NoContent();
            if (await db.SupportTicketDevices.CountAsync(x => x.TicketId == id) >= 10)
                return Results.Conflict(new { error = "Un ticket può essere collegato al massimo a 10 dispositivi." });
            var now = DateTimeOffset.UtcNow;
            var updated = await db.SupportTickets.Where(t => t.Id == id && t.Version == r.ExpectedVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Version, t => t.Version + 1).SetProperty(t => t.UpdatedAt, now));
            if (updated == 0) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica i dettagli." });
            db.SupportTicketDevices.Add(new SupportTicketDevice { TicketId = id, DeviceId = currentTarget.Id, LinkedAt = now,
                LinkedBy = userId, DeviceNameSnapshot = currentTarget.Name, SiteIdSnapshot = currentTarget.SiteId, SiteNameSnapshot = currentTarget.SiteName });
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.device.link", Target = id.ToString() });
            CoreOutbox.Add(db, "ticket.devices.updated", "ticket", id,
                new { ticketId = id, deviceId = currentTarget.Id, siteId = currentTarget.SiteId, version = r.ExpectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        staff.MapDelete("/{id:guid}/devices/{deviceId:guid}", async (Guid id, Guid deviceId, long expectedVersion, HttpContext c, AppDbContext db, PermissionService p) => {
            if (expectedVersion < 1 || deviceId == Guid.Empty) return Results.BadRequest();
            var userId = c.User.UserId();
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(userId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            if (deviceId == ticket.DeviceId) return Results.BadRequest(new { error = "Il dispositivo principale non può essere scollegato." });
            if (!await db.Devices.AnyAsync(d => d.Id == deviceId)) return Results.NotFound();
            if (!await p.Allows(userId, "tickets.manage", deviceId)) return Results.Forbid();
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var lockId in new[] { ticket.DeviceId, deviceId }.Distinct().Order())
                await DeviceOperationLock.Acquire(db, lockId);
            if (!await p.Allows(userId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId) ||
                !await p.Allows(userId, "tickets.manage", deviceId)) return Results.Forbid();
            if (!await db.SupportTicketDevices.AnyAsync(x => x.TicketId == id && x.DeviceId == deviceId))
                return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            var updated = await db.SupportTickets.Where(t => t.Id == id && t.Version == expectedVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Version, t => t.Version + 1).SetProperty(t => t.UpdatedAt, now));
            if (updated == 0) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica i dettagli." });
            await db.SupportTicketDevices.Where(x => x.TicketId == id && x.DeviceId == deviceId).ExecuteDeleteAsync();
            db.AuditEvents.Add(new AuditEvent { ActorId = userId, Action = "ticket.device.unlink", Target = id.ToString() });
            CoreOutbox.Add(db, "ticket.devices.updated", "ticket", id,
                new { ticketId = id, deviceId, version = expectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        staff.MapGet("/queues", async (Guid? siteId, HttpContext c, AppDbContext db, PermissionService p) => {
            var userId = c.User.UserId();
            var global = await p.Global(userId);
            var hasScoped = await (from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                where g.UserId == userId && (permission.Permission == "tickets.view" || permission.Permission == "tickets.manage") select g.Id).AnyAsync();
            if (!global.Contains("tickets.view") && !global.Contains("tickets.manage") && !hasScoped) return Results.Forbid();
            if (siteId is not null)
            {
                if (!await db.Sites.AnyAsync(s => s.Id == siteId)) return Results.NotFound();
                if (await p.Allows(userId, "tickets.view", siteId: siteId))
                    return Results.Ok(await db.SupportQueues.AsNoTracking()
                        .Where(x => x.IsActive && (x.SiteId == null || x.SiteId == siteId))
                        .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.SiteId }).ToArrayAsync());
                var viewGlobal = global.Contains("tickets.view");
                var viewGrants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                    where g.UserId == userId && permission.Permission == "tickets.view" select g;
                var hasVisibleTicket = await db.SupportTickets.AnyAsync(t => t.SiteId == siteId &&
                    db.Users.Any(u => u.Id == userId && !u.IsDisabled) && (viewGlobal ||
                        viewGrants.Any(g => g.SiteId == t.SiteId) || db.Devices.Any(d => d.Id == t.DeviceId &&
                            viewGrants.Any(g => g.DeviceId == d.Id || (d.GroupId != null && g.GroupId == d.GroupId)))));
                if (!hasVisibleTicket) return Results.Forbid();
            }
            return Results.Ok(await db.SupportQueues.AsNoTracking()
                .Where(x => x.IsActive && (x.SiteId == null || (siteId != null && x.SiteId == siteId)))
                .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.SiteId }).ToArrayAsync());
        });
        staff.MapGet("/{id:guid}/queues", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            return Results.Ok(await db.SupportQueues.AsNoTracking()
                .Where(q => q.IsActive && (q.SiteId == null || q.SiteId == ticket.SiteId))
                .OrderBy(q => q.Name).Select(q => new { q.Id, q.Name, q.SiteId }).ToArrayAsync());
        });
        staff.MapGet("/summary", async (Guid? deviceId, Guid? siteId, HttpContext c, AppDbContext db, PermissionService p) => {
            var userId = c.User.UserId();
            if (deviceId.HasValue && !await p.Allows(userId, "tickets.view", deviceId)) return Results.Forbid();
            if (siteId.HasValue && !await p.Allows(userId, "tickets.view", siteId: siteId)) return Results.Forbid();
            var global = (await p.Global(userId)).Contains("tickets.view");
            var grants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                         where g.UserId == userId && permission.Permission == "tickets.view" select g;
            var query = db.SupportTickets.AsNoTracking().Where(t => db.Users.Any(u => u.Id == userId && !u.IsDisabled) && (global ||
                grants.Any(g => g.SiteId == t.SiteId) || db.Devices.Any(d => d.Id == t.DeviceId && grants.Any(g => g.DeviceId == d.Id || (d.GroupId != null && g.GroupId == d.GroupId)))));
            if (deviceId.HasValue) query = query.Where(t => t.DeviceId == deviceId.Value);
            if (siteId.HasValue) query = query.Where(t => t.SiteId == siteId.Value);
            var summary = await query.GroupBy(_ => 1).Select(g => new {
                total = g.Count(),
                open = g.Count(t => t.State == "open"),
                inProgress = g.Count(t => t.State == "in_progress"),
                urgent = g.Count(t => t.Priority == "urgent" && t.State != "resolved" && t.State != "closed")
            }).SingleOrDefaultAsync();
            return Results.Ok(summary ?? new { total = 0, open = 0, inProgress = 0, urgent = 0 });
        });
        staff.MapGet("", async (string? state, string? search, Guid? deviceId, Guid? siteId, string? priority, string? assignee, string? category, string? queue, Guid? ticketId, HttpContext c, AppDbContext db, PermissionService p) => {
            if(state is not (null or "" or "open" or "in_progress" or "resolved" or "closed") || search?.Length > 200 ||
                priority is not (null or "" or "low" or "normal" or "high" or "urgent") || assignee?.Length > 320 ||
                (!string.IsNullOrEmpty(category) && !Categories.Contains(category)) ||
                (!string.IsNullOrEmpty(queue) && queue != "unassigned" && !Guid.TryParse(queue, out _))) return Results.BadRequest();
            if(deviceId.HasValue && !await p.Allows(c.User.UserId(), "tickets.view", deviceId)) return Results.Forbid();
            if(siteId.HasValue && !await p.Allows(c.User.UserId(), "tickets.view", siteId: siteId)) return Results.Forbid();
            var term=search?.Trim().ToLowerInvariant();
            var global = (await p.Global(c.User.UserId())).Contains("tickets.view");
            var grants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                         where g.UserId == c.User.UserId() && permission.Permission == "tickets.view" select g;
            var manageGlobal = (await p.Global(c.User.UserId())).Contains("tickets.manage");
            var manageGrants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                               where g.UserId == c.User.UserId() && permission.Permission == "tickets.manage" select g;
            var query = db.SupportTickets.AsNoTracking().Where(t => db.Users.Any(u => u.Id == c.User.UserId() && !u.IsDisabled) && (global ||
                grants.Any(g => g.SiteId == t.SiteId) || db.Devices.Any(d => d.Id == t.DeviceId && grants.Any(g => g.DeviceId == d.Id || (d.GroupId != null && g.GroupId == d.GroupId)))));
            if (ticketId.HasValue) query=query.Where(t=>t.Id==ticketId.Value);
            if(deviceId.HasValue)query=query.Where(t=>t.DeviceId==deviceId.Value);
            if(siteId.HasValue)query=query.Where(t=>t.SiteId==siteId.Value);
            if(!string.IsNullOrEmpty(state))query=query.Where(t=>t.State==state);
            if(!string.IsNullOrEmpty(term))query=query.Where(t=>t.Subject.ToLower().Contains(term)||db.Devices.Any(d=>d.Id==t.DeviceId&&d.Name.ToLower().Contains(term)));
            if(!string.IsNullOrEmpty(priority))query=query.Where(t=>t.Priority==priority);
            if(!string.IsNullOrEmpty(category))query=query.Where(t=>t.Category==category);
            if(queue == "unassigned")query=query.Where(t=>t.QueueId==null);
            else if(Guid.TryParse(queue, out var queueId))query=query.Where(t=>t.QueueId==queueId);
            var assigneeTerm=assignee?.Trim().ToLowerInvariant();
            if(!string.IsNullOrEmpty(assigneeTerm))query=query.Where(t=>t.AssigneeId!=null&&db.Users.Any(u=>u.Id==t.AssigneeId&&u.Email!=null&&u.Email.ToLower()==assigneeTerm));
            return Results.Ok(await query.OrderByDescending(t => t.CreatedAt).Take(100)
                .Select(t => new {t.Id,t.Number,t.DeviceId,t.SiteId,deviceName=db.Devices.Where(d=>d.Id==t.DeviceId).Select(d=>d.Name).FirstOrDefault(),siteName=db.Sites.Where(s=>s.Id==t.SiteId).Select(s=>s.Name).FirstOrDefault(),t.Subject,t.Description,t.State,t.Priority,t.Category,t.Origin,t.QueueId,queueName=t.QueueId==null?null:db.SupportQueues.Where(q=>q.Id==t.QueueId).Select(q=>q.Name).FirstOrDefault(),t.AssigneeId,assigneeEmail=t.AssigneeId==null?null:db.Users.Where(u=>u.Id==t.AssigneeId).Select(u=>u.Email).FirstOrDefault(),canManage=manageGlobal||manageGrants.Any(g=>g.SiteId==t.SiteId||g.DeviceId==t.DeviceId||(g.GroupId!=null&&db.Devices.Any(d=>d.Id==t.DeviceId&&d.GroupId==g.GroupId))),t.Resolution,t.Version,t.CreatedAt,t.UpdatedAt,hasScreenshot=t.Screenshot != null}).ToArrayAsync());
        });
        staff.MapPut("/bulk-queue", async (BulkQueueAssignment r, HttpContext c, AppDbContext db, PermissionService p) => {
            if (r.Tickets is null || r.Tickets.Length is < 1 or > 50 || r.Tickets.Any(x => x.TicketId == Guid.Empty || x.ExpectedVersion < 1) ||
                r.Tickets.Select(x => x.TicketId).Distinct().Count() != r.Tickets.Length) return Results.BadRequest();
            if (r.QueueId is not null && !await db.SupportQueues.AnyAsync(x => x.Id == r.QueueId && x.IsActive)) return Results.BadRequest(new { error = "La coda selezionata non è attiva." });
            var versions = r.Tickets.ToDictionary(x => x.TicketId, x => x.ExpectedVersion);
            var rows = await db.SupportTickets.AsNoTracking().Where(x => versions.Keys.Contains(x.Id)).ToArrayAsync();
            if (rows.Length != versions.Count) return Results.NotFound();
            foreach (var ticket in rows)
                if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var deviceId in rows.Select(x => x.DeviceId).Distinct().Order()) await DeviceOperationLock.Acquire(db, deviceId);
            foreach (var ticket in rows)
                if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            if (r.QueueId is not null && !await db.SupportQueues.AnyAsync(x => x.Id == r.QueueId && x.IsActive)) return Results.BadRequest(new { error = "La coda selezionata non è attiva." });
            rows = await db.SupportTickets.AsNoTracking().Where(x => versions.Keys.Contains(x.Id)).ToArrayAsync();
            if (rows.Any(x => x.Version != versions[x.Id])) return Results.Conflict(new { error = "Almeno un ticket è stato aggiornato. Ricarica la lista e riprova." });
            if (r.QueueId is not null)
            {
                var targetQueueSiteId = await db.SupportQueues.Where(q => q.Id == r.QueueId && q.IsActive).Select(q => q.SiteId).SingleAsync();
                if (targetQueueSiteId is not null && rows.Any(ticket => ticket.SiteId != targetQueueSiteId))
                    return Results.BadRequest(new { error = "La coda appartiene a una sede diversa da uno o più ticket selezionati." });
                var assignedIds = rows.Where(t => t.AssigneeId is not null).Select(t => t.AssigneeId!).Distinct().ToArray();
                var memberIds = await db.SupportQueueMembers.Where(m => m.QueueId == r.QueueId && assignedIds.Contains(m.UserId)).Select(m => m.UserId).ToArrayAsync();
                if (memberIds.Length != assignedIds.Length) return Results.BadRequest(new { error = "Almeno un assegnatario non appartiene alla coda selezionata." });
            }
            foreach (var ticket in rows.Where(x => x.QueueId != r.QueueId))
            {
                var nextVersion = ticket.Version + 1;
                var changed = await db.SupportTickets.Where(x => x.Id == ticket.Id && x.Version == ticket.Version)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.QueueId, r.QueueId).SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow).SetProperty(x => x.Version, nextVersion));
                if (changed == 0) return Results.Conflict(new { error = "Almeno un ticket è stato aggiornato. Ricarica la lista e riprova." });
                db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.queue.update", Target = ticket.Id.ToString() });
                CoreOutbox.Add(db, "ticket.updated", "ticket", ticket.Id,
                    new { ticketId = ticket.Id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, oldQueueId = ticket.QueueId, queueId = r.QueueId, version = nextVersion }, c.TraceIdentifier);
            }
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        staff.MapGet("/{id:guid}/assignees", async (Guid id, string? queue, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            var candidateQueueId = ticket.QueueId;
            if (queue is not null)
            {
                if (queue == "none") candidateQueueId = null;
                else if (Guid.TryParse(queue, out var requestedQueue))
                {
                    if (!await db.SupportQueues.AnyAsync(x => x.Id == requestedQueue && x.IsActive && (x.SiteId == null || x.SiteId == ticket.SiteId))) return Results.BadRequest();
                    candidateQueueId = requestedQueue;
                }
                else return Results.BadRequest();
            }
            var deviceGroupId = await db.Devices.Where(d => d.Id == ticket.DeviceId).Select(d => d.GroupId).SingleOrDefaultAsync();
            IQueryable<string> WithPermission(string permission)
            {
                var global = from ur in db.UserRoles
                             join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                             where rp.Permission == permission
                             select ur.UserId;
                var scoped = from grant in db.ScopeGrants
                             join rp in db.RolePermissions on grant.RoleId equals rp.RoleId
                             where rp.Permission == permission &&
                                 (grant.DeviceId == ticket.DeviceId || (deviceGroupId != null && grant.GroupId == deviceGroupId) || grant.SiteId == ticket.SiteId)
                             select grant.UserId;
                return global.Union(scoped);
            }
            var visible = WithPermission("tickets.view");
            var manageable = WithPermission("tickets.manage");
            var candidates = db.Users.AsNoTracking().Where(u => !u.IsDisabled && visible.Contains(u.Id) && manageable.Contains(u.Id) &&
                    (candidateQueueId == null || db.SupportQueueMembers.Any(m => m.QueueId == candidateQueueId && m.UserId == u.Id)))
                .OrderBy(u => u.Email).Take(200).Select(u => new { id = u.Id, email = u.Email });
            return Results.Ok(await candidates.ToListAsync());
        });
        staff.MapPut("/{id:guid}", async (Guid id, SupportStaffEdit r, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            if (r.State is not ("open" or "in_progress" or "resolved" or "closed")) return Results.BadRequest(new {error="Seleziona uno stato del ticket valido."});
            if (r.UpdatePriority && !Priorities.Contains(r.Priority)) return Results.BadRequest(new { error = "Seleziona una priorità valida." });
            if (r.UpdateAssignment && r.AssigneeId?.Length > 450) return Results.BadRequest();
            if(r.Resolution?.Length>8000)return Results.BadRequest(new {error="La risposta puo contenere al massimo 8000 caratteri."});
            if((r.AddReply||r.State is "resolved" or "closed")&&string.IsNullOrWhiteSpace(r.Resolution))
                return Results.BadRequest(new {error="Scrivi una risposta all'utente prima di risolvere o chiudere il ticket."});
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, ticket.DeviceId);
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            ticket=await db.SupportTickets.AsNoTracking().SingleAsync(t=>t.Id==id);
            if(ticket.Version!=r.ExpectedVersion)return Results.Conflict(new {error="Il ticket e stato aggiornato. Ricarica la conversazione prima di salvare."});
            if (r.UpdateQueue && r.QueueId is not null && !await db.SupportQueues.AnyAsync(q => q.Id == r.QueueId && q.IsActive && (q.SiteId == null || q.SiteId == ticket.SiteId)))
                return Results.BadRequest(new { error = "La coda selezionata non è attiva." });
            var queueId = r.UpdateQueue ? r.QueueId : ticket.QueueId;
            var assigneeId = r.UpdateAssignment ? r.AssigneeId : ticket.AssigneeId;
            if (r.UpdateAssignment && r.AssigneeId is not null)
            {
                if (!await db.Users.AnyAsync(u => u.Id == r.AssigneeId && !u.IsDisabled)) return Results.BadRequest(new { error = "L’assegnatario selezionato non è disponibile." });
                if (!await p.Allows(r.AssigneeId, "tickets.view", ticket.DeviceId, siteId: ticket.SiteId) ||
                    !await p.Allows(r.AssigneeId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId))
                    return Results.BadRequest(new { error = "L’assegnatario non dispone dei permessi per questa richiesta." });
            }
            if ((r.UpdateQueue || r.UpdateAssignment) && queueId is not null && assigneeId is not null && !await db.SupportQueueMembers.AnyAsync(m => m.QueueId == queueId && m.UserId == assigneeId))
                return Results.BadRequest(new { error = "L’assegnatario deve appartenere alla coda selezionata." });
            var body=r.Resolution?.Trim();
            var priority = r.UpdatePriority ? r.Priority! : ticket.Priority;
            var updated = await db.SupportTickets.Where(t => t.Id == id && t.Version == r.ExpectedVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, r.State).SetProperty(t => t.Resolution, body)
                    .SetProperty(t => t.Priority, priority).SetProperty(t => t.AssigneeId, assigneeId).SetProperty(t => t.QueueId, queueId)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow).SetProperty(t => t.Version, t => t.Version + 1));
            if (updated == 0) return Results.Conflict(new {error="Il ticket e stato aggiornato. Ricarica la conversazione prima di salvare."});
            if(!string.IsNullOrEmpty(body)&&(r.AddReply||body!=ticket.Resolution)) {
                db.SupportMessages.Add(new SupportMessage {TicketId=id,ActorId=c.User.UserId(),Body=body});
                db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action="ticket.reply",Target=id.ToString()});
            }
            if(ticket.State!=r.State)db.SupportMessages.Add(new SupportMessage {TicketId=id,ActorId=c.User.UserId(),Kind="state_change",FromState=ticket.State,ToState=r.State});
            if (ticket.Priority != priority) db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.priority.update", Target = id.ToString() });
            if (ticket.AssigneeId != assigneeId) db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.assignee.update", Target = id.ToString() });
            if (ticket.QueueId != queueId) db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.queue.update", Target = id.ToString() });
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.update", Target = id.ToString() });
            CoreOutbox.Add(db, "ticket.updated", "ticket", id,
                new { ticketId = id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, actorId = c.User.UserId(), oldState = ticket.State, state = r.State, oldPriority = ticket.Priority, priority, oldAssigneeId = ticket.AssigneeId, assigneeId, oldQueueId = ticket.QueueId, queueId, version = r.ExpectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        staff.MapPost("/{id:guid}/internal-notes", async (Guid id, SupportInternalNote r, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.view", ticket.DeviceId, siteId: ticket.SiteId) ||
                !await p.Allows(c.User.UserId(), "tickets.note.internal", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            var body = r.Body?.Trim();
            if (string.IsNullOrWhiteSpace(body) || body.Length > 8000)
                return Results.BadRequest(new { error = "La nota interna deve contenere da 1 a 8000 caratteri." });
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, ticket.DeviceId);
            if (!await p.Allows(c.User.UserId(), "tickets.view", ticket.DeviceId, siteId: ticket.SiteId) ||
                !await p.Allows(c.User.UserId(), "tickets.note.internal", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            ticket = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == id);
            if (ticket.Version != r.ExpectedVersion) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica la conversazione prima di aggiungere la nota." });
            var now = DateTimeOffset.UtcNow;
            var updated = await db.SupportTickets.Where(t => t.Id == id && t.Version == r.ExpectedVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedAt, now).SetProperty(t => t.Version, t => t.Version + 1));
            if (updated == 0) return Results.Conflict(new { error = "Il ticket è stato aggiornato. Ricarica la conversazione prima di aggiungere la nota." });
            db.SupportMessages.Add(new SupportMessage { TicketId = id, ActorId = c.User.UserId(), Kind = "internal_note", Visibility = "internal", Body = body, CreatedAt = now });
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.note.internal.created", Target = id.ToString() });
            CoreOutbox.Add(db, "ticket.internal_note.created", "ticket", id,
                new { ticketId = id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, version = r.ExpectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        staff.MapGet("/{id:guid}/messages",async(Guid id,long? before,HttpContext c,AppDbContext db,PermissionService p)=>{
            if(before is <=0)return Results.BadRequest();
            var ticket=await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id);
            if(ticket is null)return Results.NotFound();
            if(!await p.Allows(c.User.UserId(),"tickets.view",ticket.DeviceId,siteId:ticket.SiteId))return Results.Forbid();
            var canViewInternal = await p.Allows(c.User.UserId(), "tickets.note.internal", ticket.DeviceId, siteId: ticket.SiteId);
            var query=db.SupportMessages.AsNoTracking().Where(m=>m.TicketId==id && (m.Visibility=="public" || canViewInternal));
            if(before.HasValue)query=query.Where(m=>m.Id<before.Value);
            var rows=await query.OrderByDescending(m=>m.Id).Take(100)
                .Select(m=>new {m.Id,m.Kind,m.Visibility,m.Body,m.FromState,m.ToState,m.CreatedAt,m.IsImported}).ToArrayAsync();
            c.Response.Headers.CacheControl="no-store";
            var oldest=rows.Length>0?rows[^1].Id:(long?)null;
            var hasMore=oldest.HasValue&&await query.AnyAsync(m=>m.Id<oldest.Value);
            return Results.Ok(new {ticket.Version,ticket.State,ticket.Resolution,canReply=await p.Allows(c.User.UserId(),"tickets.manage",ticket.DeviceId,siteId:ticket.SiteId),canNote=canViewInternal,hasMore,oldestId=oldest,messages=rows.Reverse()});
        });
        staff.MapGet("/{id:guid}/screenshot", async (Guid id, bool? inline, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.view", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            if (ticket.Screenshot is null) return Results.NotFound();
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = inline is true ? "ticket.screenshot.view" : "ticket.screenshot.download", Target = id.ToString() });
            await db.SaveChangesAsync();
            c.Response.Headers.CacheControl="no-store";
            c.Response.Headers["X-Content-Type-Options"]="nosniff";
            return Results.File(ticket.Screenshot, "image/png", inline is true ? null : $"screenshot-{id}.png");
        });
    }

    // Narrow screenshot format only: bounded RGBA8 PNG, no metadata, animations or arbitrary attachments.
    internal static bool ValidScreenshot(byte[] data)
    {
        if (data.Length is < 57 or > 524288 || !data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) return false;
        try
        {
            var position = 8; var width = 0; var height = 0; var ended = false; var sawData = false;
            using var compressed = new MemoryStream();
            while (position <= data.Length - 12)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position,4));
                if (length > 524288 || length > data.Length - position - 12) return false;
                uint crc = 0xffffffff;
                for (var i = position+4; i < position+8+(int)length; i++)
                {
                    crc ^= data[i];
                    for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
                }
                if (~crc != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position+8+(int)length,4))) return false;
                var type = System.Text.Encoding.ASCII.GetString(data,position+4,4);
                if (position == 8)
                {
                    if (type != "IHDR" || length != 13) return false;
                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position+8,4));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position+12,4));
                    if (width is < 1 or > 1920 || height is < 1 or > 1080 || data[position+16] != 8 || data[position+17] != 6 ||
                        data[position+18] != 0 || data[position+19] != 0 || data[position+20] != 0) return false;
                }
                else if (type == "IDAT") { compressed.Write(data,position+8,(int)length); sawData = true; }
                else if (type == "IEND" && length == 0) { ended = position + 12 == data.Length; break; }
                else return false;
                position += (int)length + 12;
            }
            if (!ended || !sawData) return false;
            compressed.Position = 0;
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            var row = new byte[width * 4 + 1];
            for (var y = 0; y < height; y++) { inflate.ReadExactly(row); if (row[0] > 4) return false; }
            return inflate.ReadByte() == -1;
        }
        catch (IOException) { return false; }
    }
}
