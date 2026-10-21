using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Maintenance;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;

namespace OmniDesk.Api.Endpoints;

public sealed record DeviceAttentionItem(Guid Id, string Name, bool Online, DateTimeOffset? LastSeen,
    int? PendingUpdates, string? UpdateState, DateTimeOffset? UpdateCheckedAt,
    string[] DefenderIssues, DateTimeOffset? DefenderObservedAt);

public static class DeviceAttentionEndpoints
{
    public static void MapDeviceAttentionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard/device-attention", async (HttpContext context, AppDbContext db,
            PermissionService permissions, IOptions<DevicePresenceOptions> presence) =>
        {
            var userId = context.User.UserId();
            var cutoff = DevicePresenceOptions.Cutoff(DateTimeOffset.UtcNow, presence.Value);
            var devices = await permissions.VisibleDevices(userId).AsNoTracking()
                .Select(device => new
                {
                    device.Id,
                    device.Name,
                    device.GroupId,
                    device.SiteId,
                    Revoked = db.DeviceCredentials.Any(credential => credential.DeviceId == device.Id && credential.RevokedAt != null),
                    LastSeen = db.DeviceMetrics.Where(metric => metric.DeviceId == device.Id)
                        .Select(metric => (DateTimeOffset?)metric.ReceivedAt).FirstOrDefault()
                }).ToListAsync();

            var globalPermissions = (await permissions.Global(userId)).ToHashSet(StringComparer.Ordinal);
            var scopedPermissions = await (from grant in db.ScopeGrants.AsNoTracking()
                join permission in db.RolePermissions.AsNoTracking() on grant.RoleId equals permission.RoleId
                where grant.UserId == userId && (permission.Permission == "updates" || permission.Permission == "endpointsecurity.defender.view")
                select new { permission.Permission, grant.DeviceId, grant.GroupId, grant.SiteId }).ToListAsync();
            bool CanRead(string capability, Guid id, Guid? groupId, Guid siteId) => globalPermissions.Contains(capability) || scopedPermissions.Any(grant =>
                grant.Permission == capability && (grant.DeviceId == id ||
                    grant.GroupId is not null && grant.GroupId == groupId ||
                    grant.SiteId == siteId));

            var activeDevices = devices.Where(device => !device.Revoked).ToArray();
            var updateIds = activeDevices.Where(device => CanRead("updates", device.Id, device.GroupId, device.SiteId)).Select(device => device.Id).ToArray();
            var defenderIds = activeDevices.Where(device => CanRead("endpointsecurity.defender.view", device.Id, device.GroupId, device.SiteId)).Select(device => device.Id).ToArray();

            var latestScans = await db.UpdateJobs.AsNoTracking()
                .Where(job => updateIds.Contains(job.DeviceId) && job.Kind == "scan" && job.CompletedAt != null)
                .GroupBy(job => job.DeviceId)
                .Select(group => group.OrderByDescending(job => job.CreatedAt).Select(job => new
                {
                    job.DeviceId,
                    job.State,
                    job.Result,
                    job.CreatedAt
                }).First())
                .ToListAsync();
            var scanByDevice = latestScans.ToDictionary(scan => scan.DeviceId);

            var inventoryRows = await db.DeviceInventories.AsNoTracking()
                .Where(row => defenderIds.Contains(row.DeviceId))
                .Select(row => new { row.DeviceId, row.ReceivedAt, row.Payload })
                .ToListAsync();
            var inventoryByDevice = inventoryRows.ToDictionary(row => row.DeviceId);

            var result = new List<DeviceAttentionItem>();
            foreach (var device in activeDevices)
            {
                int? pendingUpdates = null;
                string? updateState = null;
                DateTimeOffset? updateCheckedAt = null;
                var issues = new List<string>();

                if (updateIds.Contains(device.Id))
                {
                    if (!scanByDevice.TryGetValue(device.Id, out var scan))
                    {
                        updateState = "not_scanned";
                        issues.Add("Aggiornamenti Windows non verificati");
                    }
                    else
                    {
                        updateCheckedAt = scan.CreatedAt;
                        updateState = scan.State;
                        if (scan.State == "succeeded" && scan.Result is not null)
                        {
                            try
                            {
                                pendingUpdates = JsonSerializer.Deserialize<UpdateResultRequest>(scan.Result)?.Updates?.Length ?? 0;
                            }
                            catch (JsonException)
                            {
                                updateState = "invalid_result";
                                issues.Add("Esito scansione aggiornamenti non leggibile");
                            }
                        }
                        else if (scan.State is "failed" or "expired")
                        {
                            issues.Add("Ultima scansione aggiornamenti non riuscita");
                        }
                        else if (scan.State is "queued" or "leased")
                        {
                            updateState = "running";
                        }
                    }
                }

                DateTimeOffset? defenderObservedAt = null;
                if (defenderIds.Contains(device.Id))
                {
                    if (!inventoryByDevice.TryGetValue(device.Id, out var inventoryRow))
                    {
                        issues.Add("Defender: in attesa del primo report");
                    }
                    else
                    {
                        InventoryRequest? inventory = null;
                        try { inventory = JsonSerializer.Deserialize<InventoryRequest>(inventoryRow.Payload); }
                        catch (JsonException) { }
                        var defender = inventory?.Defender;
                        defenderObservedAt = defender?.ObservedAt;
                        if (defender is null) issues.Add("Defender: report non disponibile");
                        else if (defender.ProviderState != "available") issues.Add(defender.ProviderState == "unsupported" ? "Defender: provider non disponibile" : "Defender: lettura non riuscita");
                        else if (defender.ObservedAt < DateTimeOffset.UtcNow.AddHours(-48)) issues.Add("Defender: report non aggiornato");
                        else
                        {
                            if (defender.AntivirusEnabled == false) issues.Add("Defender: antivirus disattivato");
                            if (defender.RealTimeProtectionEnabled == false) issues.Add("Defender: protezione in tempo reale disattivata");
                            if (defender.TamperProtection == false) issues.Add("Defender: Tamper Protection disattivata");
                        }
                    }
                }

                if (issues.Count > 0)
                    result.Add(new(device.Id, device.Name, device.LastSeen > cutoff, device.LastSeen,
                        pendingUpdates, updateState, updateCheckedAt, issues.ToArray(), defenderObservedAt));
            }
            return Results.Ok(result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
        }).RequireAuthorization();
    }
}
