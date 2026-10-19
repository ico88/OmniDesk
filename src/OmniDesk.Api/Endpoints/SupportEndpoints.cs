using System.IO.Compression;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;

namespace OmniDesk.Api.Endpoints;

public sealed record SupportStaffEdit(long ExpectedVersion,string State,string? Resolution,bool AddReply=false,
    string? Priority=null,bool UpdatePriority=false,string? AssigneeId=null,bool UpdateAssignment=false);
public static class SupportEndpoints
{
    private static readonly string[] Priorities = ["low", "normal", "high", "urgent"];
    private static bool ValidKey(string? key) => key is { Length: 64 } && key.All(Uri.IsHexDigit);
    private static SupportTicketView View(SupportTicket t) => new(t.Id, t.Number, t.DeviceId, t.Subject, t.Description, t.State,
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
                .Select(t => new SupportTicketView(t.Id,t.Number,t.DeviceId,t.Subject,"",t.State,t.Resolution,t.Version,t.CreatedAt,t.UpdatedAt,t.Screenshot != null)).ToArrayAsync();
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
            var siteId = await db.Devices.Where(x => x.Id == id).Select(x => x.SiteId).SingleOrDefaultAsync();
            if (siteId == Guid.Empty) return Results.Conflict();
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
            var ticket = new SupportTicket { DeviceId = id, SiteId = siteId, RequesterKey = r.RequesterKey, RequestId = r.Ticket.RequestId,
                Subject = r.Ticket.Subject.Trim(), Description = r.Ticket.Description.Trim(), Screenshot = screenshot };
            db.SupportTickets.Add(ticket);
            CoreOutbox.Add(db, "ticket.created", "ticket", ticket.Id,
                new { ticketId = ticket.Id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, state = ticket.State }, c.TraceIdentifier);
            db.AuditEvents.Add(new AuditEvent { Action = "ticket.create", Target = ticket.Id.ToString() });
            if (screenshot != null) db.AuditEvents.Add(new AuditEvent { Action = "ticket.screenshot.consent", Target = ticket.Id.ToString() });
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/support/{ticket.Id}", View(ticket));
        });
        var staff = app.MapGroup("/api/support").RequireAuthorization();
        staff.MapGet("", async (string? state, string? search, Guid? deviceId, Guid? siteId, string? priority, string? assignee, HttpContext c, AppDbContext db, PermissionService p) => {
            if(state is not (null or "" or "open" or "in_progress" or "resolved" or "closed") || search?.Length > 200 ||
                priority is not (null or "" or "low" or "normal" or "high" or "urgent") || assignee?.Length > 320) return Results.BadRequest();
            if(deviceId.HasValue && !await p.Allows(c.User.UserId(), "tickets.view", deviceId)) return Results.Forbid();
            if(siteId.HasValue && !await p.Allows(c.User.UserId(), "tickets.view", siteId: siteId)) return Results.Forbid();
            var term=search?.Trim().ToLowerInvariant();
            var global = (await p.Global(c.User.UserId())).Contains("tickets.view");
            var grants = from g in db.ScopeGrants join permission in db.RolePermissions on g.RoleId equals permission.RoleId
                         where g.UserId == c.User.UserId() && permission.Permission == "tickets.view" select g;
            var query = db.SupportTickets.AsNoTracking().Where(t => db.Users.Any(u => u.Id == c.User.UserId() && !u.IsDisabled) && (global ||
                grants.Any(g => g.SiteId == t.SiteId) || db.Devices.Any(d => d.Id == t.DeviceId && grants.Any(g => g.DeviceId == d.Id || (d.GroupId != null && g.GroupId == d.GroupId)))));
            if(deviceId.HasValue)query=query.Where(t=>t.DeviceId==deviceId.Value);
            if(siteId.HasValue)query=query.Where(t=>t.SiteId==siteId.Value);
            if(!string.IsNullOrEmpty(state))query=query.Where(t=>t.State==state);
            if(!string.IsNullOrEmpty(term))query=query.Where(t=>t.Subject.ToLower().Contains(term)||db.Devices.Any(d=>d.Id==t.DeviceId&&d.Name.ToLower().Contains(term)));
            if(!string.IsNullOrEmpty(priority))query=query.Where(t=>t.Priority==priority);
            var assigneeTerm=assignee?.Trim().ToLowerInvariant();
            if(!string.IsNullOrEmpty(assigneeTerm))query=query.Where(t=>t.AssigneeId!=null&&db.Users.Any(u=>u.Id==t.AssigneeId&&u.Email!=null&&u.Email.ToLower()==assigneeTerm));
            return Results.Ok(await query.OrderByDescending(t => t.CreatedAt).Take(100)
                .Select(t => new {t.Id,t.Number,t.DeviceId,t.SiteId,deviceName=db.Devices.Where(d=>d.Id==t.DeviceId).Select(d=>d.Name).FirstOrDefault(),siteName=db.Sites.Where(s=>s.Id==t.SiteId).Select(s=>s.Name).FirstOrDefault(),t.Subject,t.Description,t.State,t.Priority,t.AssigneeId,assigneeEmail=t.AssigneeId==null?null:db.Users.Where(u=>u.Id==t.AssigneeId).Select(u=>u.Email).FirstOrDefault(),t.Resolution,t.Version,t.CreatedAt,t.UpdatedAt,hasScreenshot=t.Screenshot != null}).ToArrayAsync());
        });
        staff.MapGet("/{id:guid}/assignees", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
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
            var candidates = db.Users.AsNoTracking().Where(u => !u.IsDisabled && visible.Contains(u.Id) && manageable.Contains(u.Id))
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
            if (r.UpdateAssignment && r.AssigneeId is not null)
            {
                if (!await db.Users.AnyAsync(u => u.Id == r.AssigneeId && !u.IsDisabled)) return Results.BadRequest(new { error = "L’assegnatario selezionato non è disponibile." });
                if (!await p.Allows(r.AssigneeId, "tickets.view", ticket.DeviceId, siteId: ticket.SiteId) ||
                    !await p.Allows(r.AssigneeId, "tickets.manage", ticket.DeviceId, siteId: ticket.SiteId))
                    return Results.BadRequest(new { error = "L’assegnatario non dispone dei permessi per questa richiesta." });
            }
            var body=r.Resolution?.Trim();
            var priority = r.UpdatePriority ? r.Priority! : ticket.Priority;
            var assigneeId = r.UpdateAssignment ? r.AssigneeId : ticket.AssigneeId;
            var updated = await db.SupportTickets.Where(t => t.Id == id && t.Version == r.ExpectedVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, r.State).SetProperty(t => t.Resolution, body)
                    .SetProperty(t => t.Priority, priority).SetProperty(t => t.AssigneeId, assigneeId)
                    .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow).SetProperty(t => t.Version, t => t.Version + 1));
            if (updated == 0) return Results.Conflict(new {error="Il ticket e stato aggiornato. Ricarica la conversazione prima di salvare."});
            if(!string.IsNullOrEmpty(body)&&(r.AddReply||body!=ticket.Resolution)) {
                db.SupportMessages.Add(new SupportMessage {TicketId=id,ActorId=c.User.UserId(),Body=body});
                db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action="ticket.reply",Target=id.ToString()});
            }
            if(ticket.State!=r.State)db.SupportMessages.Add(new SupportMessage {TicketId=id,ActorId=c.User.UserId(),Kind="state_change",FromState=ticket.State,ToState=r.State});
            if (ticket.Priority != priority) db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.priority.update", Target = id.ToString() });
            if (ticket.AssigneeId != assigneeId) db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.assignee.update", Target = id.ToString() });
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "ticket.update", Target = id.ToString() });
            CoreOutbox.Add(db, "ticket.updated", "ticket", id,
                new { ticketId = id, deviceId = ticket.DeviceId, siteId = ticket.SiteId, oldState = ticket.State, state = r.State, oldPriority = ticket.Priority, priority, oldAssigneeId = ticket.AssigneeId, assigneeId, version = r.ExpectedVersion + 1 }, c.TraceIdentifier);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        staff.MapGet("/{id:guid}/messages",async(Guid id,long? before,HttpContext c,AppDbContext db,PermissionService p)=>{
            if(before is <=0)return Results.BadRequest();
            var ticket=await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id);
            if(ticket is null)return Results.NotFound();
            if(!await p.Allows(c.User.UserId(),"tickets.view",ticket.DeviceId,siteId:ticket.SiteId))return Results.Forbid();
            var query=db.SupportMessages.AsNoTracking().Where(m=>m.TicketId==id);
            if(before.HasValue)query=query.Where(m=>m.Id<before.Value);
            var rows=await query.OrderByDescending(m=>m.Id).Take(100)
                .Select(m=>new {m.Id,m.Kind,m.Body,m.FromState,m.ToState,m.CreatedAt,m.IsImported}).ToArrayAsync();
            c.Response.Headers.CacheControl="no-store";
            var oldest=rows.Length>0?rows[^1].Id:(long?)null;
            var hasMore=oldest.HasValue&&await query.AnyAsync(m=>m.Id<oldest.Value);
            return Results.Ok(new {ticket.Version,ticket.State,ticket.Resolution,canReply=await p.Allows(c.User.UserId(),"tickets.manage",ticket.DeviceId,siteId:ticket.SiteId),hasMore,oldestId=oldest,messages=rows.Reverse()});
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
