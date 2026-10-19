using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;
public static class Permissions
{
    public static readonly string[] Roles = ["Administrator", "Operator", "Viewer"];
    public static readonly string[] All = ["view", "remote.control", "terminal", "files", "updates", "diagnostics.view", "services.manage", "processes.manage", "administration", "tickets.view", "tickets.manage"];
    public static string[] ForRole(string role) => role switch
    {
        "Administrator" => All,
        "Operator" => ["view", "remote.control", "terminal", "files", "updates", "diagnostics.view", "tickets.view", "tickets.manage"],
        "Viewer" => ["view"],
        _ => []
    };
}
public sealed class PermissionService(AppDbContext db)
{
    public Task<string[]> Global(string userId) => (
        from ur in db.UserRoles
        join u in db.Users on ur.UserId equals u.Id
        join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
        where ur.UserId == userId && !u.IsDisabled
        select rp.Permission).Distinct().ToArrayAsync();

    public async Task<bool> Allows(string userId, string permission, Guid? deviceId = null, Guid? groupId = null, Guid? siteId = null)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDisabled)) return false;
        if ((await Global(userId)).Contains(permission)) return true;
        if (deviceId is not null)
        {
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId);
            if (device is null) return false;
            groupId ??= device.GroupId;
            siteId ??= device.SiteId;
        }
        if (deviceId is null && groupId is null && siteId is null) return false;
        return await (from g in db.ScopeGrants
                      join p in db.RolePermissions on g.RoleId equals p.RoleId
                      where g.UserId == userId && p.Permission == permission &&
                      ((deviceId != null && g.DeviceId == deviceId) || (groupId != null && g.GroupId == groupId) || (siteId != null && g.SiteId == siteId))
                      select g.Id).AnyAsync();
    }
    public IQueryable<Device> VisibleDevices(string userId) => db.Devices.Where(d => db.Users.Any(u => u.Id == userId && !u.IsDisabled) && (
        (from ur in db.UserRoles join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
         where ur.UserId == userId && rp.Permission == "view" select ur.RoleId).Any() ||
        (from g in db.ScopeGrants join rp in db.RolePermissions on g.RoleId equals rp.RoleId
         where g.UserId == userId && rp.Permission == "view" &&
         (g.DeviceId == d.Id || (d.GroupId != null && g.GroupId == d.GroupId) || g.SiteId == d.SiteId) select g.Id).Any()));
}
public static class UserIdentity
{
    public static string UserId(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
