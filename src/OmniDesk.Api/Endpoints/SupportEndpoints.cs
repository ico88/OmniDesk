using System.IO.Compression;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;

namespace OmniDesk.Api.Endpoints;

public static class SupportEndpoints
{
    private static bool ValidKey(string? key) => key is { Length: 64 } && key.All(Uri.IsHexDigit);
    private static SupportTicketView View(SupportTicket t) => new(t.Id, t.DeviceId, t.Subject, t.Description, t.State,
        t.Resolution, t.Version, t.CreatedAt, t.UpdatedAt, t.Screenshot != null);
    public static void MapSupportEndpoints(this WebApplication app)
    {
        var machine = app.MapGroup("/api/agent/support").WithMetadata(new MachineRequest())
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = DeviceAuthentication.SchemeName });
        machine.MapGet("/{requesterKey}", async (string requesterKey, HttpContext c, AppDbContext db) => {
            if (!ValidKey(requesterKey)) return Results.BadRequest();
            var deviceId = Guid.Parse(c.User.UserId());
            var rows = await db.SupportTickets.AsNoTracking().Where(t => t.DeviceId == deviceId && t.RequesterKey == requesterKey)
                .OrderByDescending(t => t.CreatedAt).Take(10)
                .Select(t => new SupportTicketView(t.Id,t.DeviceId,t.Subject,"",t.State,t.Resolution,t.Version,t.CreatedAt,t.UpdatedAt,t.Screenshot != null)).ToArrayAsync();
            return Results.Ok(rows);
        });
        machine.MapPost("", async (SupportIntake r, HttpContext c, AppDbContext db) => {
            if (!ValidKey(r.RequesterKey) || r.Ticket is null || r.Ticket.RequestId == Guid.Empty ||
                string.IsNullOrWhiteSpace(r.Ticket.Subject) || r.Ticket.Subject.Length > 200 ||
                string.IsNullOrWhiteSpace(r.Ticket.Description) || r.Ticket.Description.Length > 8000) return Results.BadRequest();
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
            var previous = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.DeviceId == id && t.RequesterKey == r.RequesterKey && t.RequestId == r.Ticket.RequestId);
            if (previous != null)
            {
                if (previous.Subject != r.Ticket.Subject.Trim() || previous.Description != r.Ticket.Description.Trim() ||
                    !(previous.Screenshot ?? []).SequenceEqual(screenshot ?? [])) return Results.Conflict();
                return Results.Ok(View(previous));
            }
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
            if (await db.SupportTickets.CountAsync(t => t.DeviceId == id && t.CreatedAt > cutoff) >= 10)
                return Results.StatusCode(429);
            var ticket = new SupportTicket { DeviceId = id, RequesterKey = r.RequesterKey, RequestId = r.Ticket.RequestId,
                Subject = r.Ticket.Subject.Trim(), Description = r.Ticket.Description.Trim(), Screenshot = screenshot };
            db.SupportTickets.Add(ticket);
            db.AuditEvents.Add(new AuditEvent { Action = "ticket.create", Target = ticket.Id.ToString() });
            if (screenshot != null) db.AuditEvents.Add(new AuditEvent { Action = "ticket.screenshot.consent", Target = ticket.Id.ToString() });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/support/{ticket.Id}", View(ticket));
        });
        var staff = app.MapGroup("/api/support").RequireAuthorization();
        staff.MapGet("", async (HttpContext c, AppDbContext db, PermissionService p) => {
            var global = (await p.Global(c.User.UserId())).Contains("tickets.view");
            var grants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                         where g.UserId == c.User.UserId() && permission.Permission == "tickets.view" select g;
            var query = db.SupportTickets.AsNoTracking().Where(t => db.Users.Any(u => u.Id == c.User.UserId() && !u.IsDisabled) && (global || db.Devices.Any(d => d.Id == t.DeviceId &&
                grants.Any(g => g.DeviceId == d.Id || (d.GroupId != null && g.GroupId == d.GroupId)))));
            return Results.Ok(await query.OrderByDescending(t => t.CreatedAt).Take(100)
                .Select(t => new SupportTicketView(t.Id,t.DeviceId,t.Subject,t.Description,t.State,t.Resolution,t.Version,t.CreatedAt,t.UpdatedAt,t.Screenshot != null)).ToArrayAsync());
        });
        staff.MapPut("/{id:guid}", async (Guid id, SupportEdit r, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId)) return Results.Forbid();
            if (r.State is not ("open" or "in_progress" or "resolved" or "closed") || r.Resolution?.Length > 8000 ||
                (r.State is "resolved" or "closed" && string.IsNullOrWhiteSpace(r.Resolution))) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, ticket.DeviceId);
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId)) return Results.Forbid();
            var updated = await db.SupportTickets.Where(t => t.Id == id && t.Version == r.ExpectedVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, r.State).SetProperty(t => t.Resolution, r.Resolution)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow).SetProperty(t => t.Version, t => t.Version + 1));
            if (updated == 0) return Results.Conflict();
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.update", Target = id.ToString() });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        staff.MapGet("/{id:guid}/screenshot", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.view", ticket.DeviceId)) return Results.Forbid();
            if (ticket.Screenshot is null) return Results.NotFound();
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.screenshot.download", Target = id.ToString() });
            await db.SaveChangesAsync();
            return Results.File(ticket.Screenshot, "image/png", $"screenshot-{id}.png");
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
