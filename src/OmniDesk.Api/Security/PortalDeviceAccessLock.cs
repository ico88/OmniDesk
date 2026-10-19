using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;

public static class PortalDeviceAccessLock
{
    // Call inside a transaction. All portal ticket mutations and link revocations use user→device lock order.
    public static async Task Acquire(AppDbContext db, string userId, Guid deviceId)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"omnidesk-portal-user-" + userId}, 0))");
        await DeviceOperationLock.Acquire(db, deviceId);
    }
}
