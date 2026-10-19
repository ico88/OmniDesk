using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;

public static class DeviceOperationLock
{
    // Caller owns a transaction. Use the same lock for metadata moves, revocation and job queue/claim.
    public static Task Acquire(AppDbContext db, Guid deviceId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"omnidesk-device-" + deviceId}, 0))");
}
