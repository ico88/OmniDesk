using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Remote.MeshCentral;

namespace OmniDesk.Api.Security;

public sealed class RemoteSessionGuard(IServiceScopeFactory scopes, ILogger<RemoteSessionGuard> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await Sweep(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e) { logger.LogWarning("Remote session guard failed: {Type}", e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task Sweep(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var permissions = scope.ServiceProvider.GetRequiredService<PermissionService>();
        var mesh = scope.ServiceProvider.GetRequiredService<IMeshCentralClient>();
        var sessions = await db.RemoteSessions.AsNoTracking().Where(x => x.State == "active" || x.State == "revocation_pending").OrderBy(x => x.CreatedAt).ToListAsync(ct);
        foreach (var session in sessions)
        {
            if (session.State == "active" && session.ExpiresAt > DateTimeOffset.UtcNow && await permissions.Allows(session.ActorId, session.Capability, session.DeviceId)
                && !await db.DeviceCredentials.AnyAsync(x => x.DeviceId == session.DeviceId && x.RevokedAt != null, ct)
                && !await db.RemoteProvisionings.AnyAsync(x => x.DeviceId == session.DeviceId && !x.Enabled, ct)) continue;
            if (session.State == "active")
            {
                var claimed = await db.RemoteSessions.Where(x => x.Id == session.Id && x.State == "active").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revocation_pending"), ct);
                if (claimed == 0) continue;
            }
            // Keep durable pending state until the engine confirms revocation; retry after restart/failure.
            try
            {
                if (!mesh.Configured || string.IsNullOrEmpty(session.NodeId) || session.PublicId is null) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await mesh.Revoke(session.NodeId, session.PublicId, timeout.Token);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var updated = await db.RemoteSessions.Where(x => x.Id == session.Id && x.State == "revocation_pending").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revoked"), ct);
                if (updated > 0)
                {
                    db.AuditEvents.Add(new AuditEvent { Action = "remote.guard.revoke", Target = session.Id.ToString() });
                    await db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning("Remote revocation pending for {SessionId}: {Type}", session.Id, e.GetType().Name); }
        }
    }
}
