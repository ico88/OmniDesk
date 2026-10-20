using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;

public static class TicketAccessPolicy
{
    public static async Task<bool> CanViewAsync(AppDbContext db, PermissionService permissions, string userId,
        Guid ticketId, string? requesterUserId, Guid deviceId, Guid siteId, CancellationToken ct)
    {
        if (await permissions.Allows(userId, "tickets.view", deviceId, siteId: siteId)) return true;
        if (!StringComparer.Ordinal.Equals(requesterUserId, userId)) return false;

        return await db.Users.AnyAsync(user => user.Id == userId && !user.IsDisabled, ct) &&
            await db.SupportTickets.AnyAsync(ticket => ticket.Id == ticketId && ticket.RequesterUserId == userId, ct) &&
            await db.PortalDeviceAccesses.AnyAsync(access => access.UserId == userId && access.DeviceId == deviceId, ct) &&
            await db.DeviceCredentials.AnyAsync(credential => credential.DeviceId == deviceId && credential.RevokedAt == null, ct) &&
            await db.Sites.AnyAsync(site => site.Id == siteId && !site.IsArchived, ct);
    }
}
