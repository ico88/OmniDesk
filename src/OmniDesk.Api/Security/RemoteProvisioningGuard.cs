using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Remote.MeshCentral;
namespace OmniDesk.Api.Security;

public sealed class RemoteProvisioningGuard(IServiceScopeFactory scopes,ILogger<RemoteProvisioningGuard> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        try {
            while(await timer.WaitForNextTickAsync(ct)) {
                try {await Sweep(ct);}
                catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
                catch(Exception e){logger.LogWarning("Remote provisioning revocation pending: {Type}",e.GetType().Name);}
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
    }
    public async Task Sweep(CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mesh=scope.ServiceProvider.GetRequiredService<IMeshProvisioningClient>();
        var protector=scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("OmniDesk.RemoteProvisioning.v1");
        var devices=await db.RemoteProvisionings.AsNoTracking().Where(p=>!p.GroupRemoved&&(!p.Enabled||db.DeviceCredentials.Any(c=>c.DeviceId==p.DeviceId&&c.RevokedAt!=null))).Select(p=>p.DeviceId).ToArrayAsync(ct);
        foreach(var device in devices){
            await using var tx=await db.Database.BeginTransactionAsync(ct);await DeviceOperationLock.Acquire(db,device);
            var row=await db.RemoteProvisionings.FindAsync([device],ct);
            if(row is null||row.GroupRemoved||(row.Enabled&&!await db.DeviceCredentials.AnyAsync(c=>c.DeviceId==device&&c.RevokedAt!=null,ct)))continue;
            row.Enabled=false;
            // Persist disable independently of a failed engine operation; removal is retried.
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            // Revoke known live shares first so their explicit confirmations remain available.
            if(await db.RemoteSessions.AnyAsync(s=>s.DeviceId==device&&(s.State=="active"||s.State=="revocation_pending"),ct))continue;
            await using var removal=await db.Database.BeginTransactionAsync(ct);await DeviceOperationLock.Acquire(db,device);
            await db.Entry(row).ReloadAsync(ct);if(row.Enabled||row.GroupRemoved)continue;
            await mesh.RemoveGroup(protector.Unprotect(row.ProtectedMeshId),ct);
            row.GroupRemoved=true;row.ProtectedSettings="";
            db.AuditEvents.Add(new AuditEvent{Action="remote.group.revoke",Target=device.ToString()});
            await db.SaveChangesAsync(ct);await removal.CommitAsync(ct);
        }
    }
}
