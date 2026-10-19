using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public sealed record UserStatusRequest(bool IsDisabled);
public sealed record UserRolesRequest(string[] Roles);

public static class UserLifecycleEndpoints
{
    public static void MapUserLifecycleEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/users").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
            await ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(ctx.HttpContext.User.UserId(), "administration")
                ? await next(ctx) : Results.Forbid());

        admin.MapPut("/{id}/status", async (string id, UserStatusRequest r, HttpContext c, AppDbContext db, UserManager<AppUser> users, PermissionService permissions) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockLifecycle(db);
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (r.IsDisabled && id == c.User.UserId()) return Results.Conflict(new { error = "Non puoi sospendere il tuo account." });
            if (user.IsDisabled == r.IsDisabled) return Results.NoContent();
            if (r.IsDisabled && await IsAdministrator(db, id) && !await HasOtherAdministrator(db, id))
                return Results.Conflict(new { error = "Deve rimanere almeno un amministratore attivo." });
            user.IsDisabled = r.IsDisabled;
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return Results.Conflict();
            if (r.IsDisabled)
                await db.RemoteSessions.Where(s => s.ActorId == id && s.State == "active").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revocation_pending"));
            db.AuditEvents.Add(Event(c, r.IsDisabled ? "user.disable" : "user.enable", id));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.NoContent();
        });

        admin.MapPut("/{id}/roles", async (string id, UserRolesRequest r, HttpContext c, AppDbContext db, UserManager<AppUser> users, PermissionService permissions) => {
            if (r.Roles is null || r.Roles.Length > 20 || r.Roles.Any(string.IsNullOrWhiteSpace) || r.Roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Roles.Length)
                return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockLifecycle(db);
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            var roles = await db.Roles.Where(x => r.Roles.Contains(x.Name!)).Select(x => new { x.Id, x.Name }).ToListAsync();
            if (roles.Count != r.Roles.Length) return Results.BadRequest();
            var roleIds = roles.Select(x => x.Id).ToArray();
            var retainsAdministration = await db.RolePermissions.AnyAsync(p => roleIds.Contains(p.RoleId) && p.Permission == "administration");
            if (!user.IsDisabled && await IsAdministrator(db, id) && !retainsAdministration && !await HasOtherAdministrator(db, id))
                return Results.Conflict(new { error = "Deve rimanere almeno un amministratore attivo." });
            var existing = await users.GetRolesAsync(user);
            var remove = existing.Except(r.Roles).ToArray();
            var add = r.Roles.Except(existing).ToArray();
            if (remove.Length == 0 && add.Length == 0) return Results.NoContent();
            if (remove.Length > 0 && !(await users.RemoveFromRolesAsync(user, remove)).Succeeded) return Results.Conflict();
            if (add.Length > 0 && !(await users.AddToRolesAsync(user, add)).Succeeded) return Results.Conflict();
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return Results.Conflict();
            db.AuditEvents.Add(Event(c, "user.roles.change", id));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.NoContent();
        });

        admin.MapPost("/{id}/revoke-sessions", async (string id, HttpContext c, AppDbContext db, UserManager<AppUser> users, PermissionService permissions) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockLifecycle(db);
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return Results.Conflict();
            db.AuditEvents.Add(Event(c, "user.sessions.revoke", id));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.NoContent();
        });
    }

    // Serialize account-status/global-role mutations across API instances, including the last-admin check.
    private static Task LockLifecycle(AppDbContext db) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(679210341)");
    private static Task<bool> IsAdministrator(AppDbContext db, string id) =>
        (from ur in db.UserRoles join p in db.RolePermissions on ur.RoleId equals p.RoleId
         where ur.UserId == id && p.Permission == "administration" select ur.UserId).AnyAsync();
    private static Task<bool> HasOtherAdministrator(AppDbContext db, string id) =>
        (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join p in db.RolePermissions on ur.RoleId equals p.RoleId
         where u.Id != id && !u.IsDisabled && p.Permission == "administration" select u.Id).AnyAsync();
    private static AuditEvent Event(HttpContext c, string action, string id) => new() { ActorId = c.User.UserId(), Action = action, Target = id };
}
