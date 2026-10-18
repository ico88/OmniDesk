using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;
public sealed record BrandingRequest(string Name, string LogoUrl, string PrimaryColor, string Theme);
public sealed record CreateUserRequest(string Email, string Password, string? Role);
public sealed record CreateRoleRequest(string Name, string[] Permissions);
public sealed record GrantRequest(string UserId, string Role, Guid? GroupId, Guid? DeviceId);
public static class ManagementEndpoints
{
    public static void MapManagementEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/devices", async (HttpContext c, PermissionService p) =>
            Results.Ok(await p.VisibleDevices(c.User.UserId()).AsNoTracking().Select(x => new { x.Id, x.Name, x.GroupId }).ToListAsync()));
        api.MapGet("/branding", async (AppDbContext db) => Results.Ok(await db.Branding.AsNoTracking().SingleAsync()));
        api.MapPut("/branding", async (BrandingRequest r, HttpContext c, AppDbContext db, PermissionService p) => {
            if (!await p.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100 || r.LogoUrl is null || r.LogoUrl.Length > 2048 ||
                (r.LogoUrl.Length > 0 && (!Uri.TryCreate(r.LogoUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))) ||
                r.PrimaryColor is null || !Regex.IsMatch(r.PrimaryColor, "^#[0-9a-fA-F]{6}$") || r.Theme is not ("light" or "dark")) return Results.BadRequest();
            var branding = await db.Branding.SingleAsync();
            branding.Name = r.Name.Trim(); branding.LogoUrl = r.LogoUrl; branding.PrimaryColor = r.PrimaryColor; branding.Theme = r.Theme;
            db.AuditEvents.Add(Event(c, "branding.update", "1"));
            await db.SaveChangesAsync();
            return Results.Ok(branding);
        });
        var admin = api.MapGroup("/admin").AddEndpointFilter(async (ctx, next) => {
            var p = ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>();
            return await p.Allows(ctx.HttpContext.User.UserId(), "administration") ? await next(ctx) : Results.Forbid();
        });
        admin.MapGet("/roles", async (AppDbContext db) => {
            var roles = await db.Roles.AsNoTracking().ToListAsync();
            var permissions = await db.RolePermissions.AsNoTracking().ToListAsync();
            return Results.Ok(roles.Select(r => new { r.Id, r.Name, permissions = permissions.Where(p => p.RoleId == r.Id).Select(p => p.Permission) }));
        });
        admin.MapPost("/roles", async (CreateRoleRequest r, HttpContext c, AppDbContext db, RoleManager<IdentityRole> roles) => {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80 || r.Permissions is null || r.Permissions.Length == 0 ||
                r.Permissions.Any(p => !Permissions.All.Contains(p))) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            var role = new IdentityRole(r.Name.Trim());
            var result = await roles.CreateAsync(role);
            if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors.Select(x => x.Code) });
            db.RolePermissions.AddRange(r.Permissions.Distinct().Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
            db.AuditEvents.Add(Event(c, "role.create", role.Id));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/admin/roles/{role.Id}", new { role.Id, role.Name });
        });
        admin.MapGet("/users", async (AppDbContext db) => {
            var users = await db.Users.AsNoTracking().Select(x => new { x.Id, x.Email }).ToListAsync();
            var roles = await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id select new { ur.UserId, role.Name }).ToListAsync();
            return Results.Ok(users.Select(u => new { u.Id, u.Email, roles = roles.Where(r => r.UserId == u.Id).Select(r => r.Name) }));
        });
        admin.MapPost("/users", async (CreateUserRequest r, HttpContext c, AppDbContext db, UserManager<AppUser> users) => {
            if ((r.Role is not null && !await db.Roles.AnyAsync(x => x.Name == r.Role)) || string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 254 || string.IsNullOrEmpty(r.Password) || r.Password.Length > 1024) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            var user = new AppUser { UserName = r.Email, Email = r.Email };
            var result = await users.CreateAsync(user, r.Password);
            if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors.Select(x => x.Code) });
            if (r.Role is not null)
            {
                result = await users.AddToRoleAsync(user, r.Role);
                if (!result.Succeeded) return Results.Conflict();
            }
            db.AuditEvents.Add(Event(c, "user.create", user.Id));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.Email });
        });
        admin.MapGet("/grants", async (AppDbContext db) => Results.Ok(await db.ScopeGrants.AsNoTracking().ToListAsync()));
        admin.MapPost("/grants", async (GrantRequest r, HttpContext c, AppDbContext db) => {
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Name == r.Role);
            if (role is null || (r.GroupId is null) == (r.DeviceId is null)) return Results.BadRequest();
            if (!await db.Users.AnyAsync(x => x.Id == r.UserId) ||
                (r.GroupId is not null && !await db.DeviceGroups.AnyAsync(x => x.Id == r.GroupId)) ||
                (r.DeviceId is not null && !await db.Devices.AnyAsync(x => x.Id == r.DeviceId))) return Results.BadRequest();
            if (await db.ScopeGrants.AnyAsync(x => x.UserId == r.UserId && x.RoleId == role.Id && x.GroupId == r.GroupId && x.DeviceId == r.DeviceId)) return Results.Conflict();
            var grant = new ScopeGrant { UserId = r.UserId, RoleId = role.Id, GroupId = r.GroupId, DeviceId = r.DeviceId };
            db.ScopeGrants.Add(grant); db.AuditEvents.Add(Event(c, "grant.create", grant.Id.ToString()));
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            return Results.Created($"/api/admin/grants/{grant.Id}", new { grant.Id });
        });
        admin.MapDelete("/grants/{id:guid}", async (Guid id, HttpContext c, AppDbContext db) => {
            var grant = await db.ScopeGrants.FindAsync(id);
            if (grant is null) return Results.NotFound();
            db.ScopeGrants.Remove(grant); db.AuditEvents.Add(Event(c, "grant.revoke", id.ToString()));
            await db.SaveChangesAsync(); return Results.NoContent();
        });
        admin.MapGet("/audit", async (AppDbContext db) => Results.Ok(await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Take(100).ToListAsync()));
    }
    private static AuditEvent Event(HttpContext c, string action, string target) => new() { ActorId = c.User.UserId(), Action = action, Target = target };
}
