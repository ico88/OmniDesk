using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;
using OmniDesk.Remote.MeshCentral;
namespace OmniDesk.Api.Endpoints;

public sealed record RemoteAccessPolicyRequest(bool UnattendedAllowed, long ExpectedVersion);
public static class RemoteProvisioningEndpoints
{
    public static void MapRemoteProvisioningEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/remote-engine/health",async(HttpContext c,PermissionService p,IMeshProvisioningClient mesh)=>{
            if(!await p.Allows(c.User.UserId(),"administration"))return Results.Forbid();
            try {await mesh.CheckReady(c.RequestAborted);return Results.Ok(new{controlReady=true});}
            catch(Exception e)when(e is not OperationCanceledException){return Results.Problem("Remote engine unavailable",statusCode:503);}
        }).RequireAuthorization();
        var admin = app.MapGroup("/api/admin/devices/{id:guid}/remote-provisioning").RequireAuthorization();
        admin.MapGet("", async (Guid id,HttpContext c,AppDbContext db,PermissionService p) => {
            if (!await p.Allows(c.User.UserId(),"administration")) return Results.Forbid();
            var row=await db.RemoteProvisionings.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==id);
            return Results.Ok(new {enabled=row?.Enabled??false,id=row?.Id,createdAt=row?.CreatedAt,unattendedAllowed=row?.UnattendedAllowed??false,accessPolicyVersion=row?.AccessPolicyVersion??0});
        });
        admin.MapPost("", async (Guid id,HttpContext c,AppDbContext db,PermissionService p,IMeshProvisioningClient mesh,IDataProtectionProvider protection,ILoggerFactory logs) => {
            if (!await p.Allows(c.User.UserId(),"administration")) return Results.Forbid();
            if (!mesh.Configured) return Results.Problem("Remote engine unavailable",statusCode:503);
            await using var tx=await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db,id);
            if (!await p.Allows(c.User.UserId(),"administration")) return Results.Forbid();
            if (!await db.DeviceCredentials.AnyAsync(x=>x.DeviceId==id&&x.RevokedAt==null)) return Results.BadRequest();
            var row=await db.RemoteProvisionings.FindAsync(id);
            if (row is {Enabled:true}) return Results.NoContent();
            try {
                var result=await mesh.Provision(id,c.RequestAborted);
                var protector=protection.CreateProtector("OmniDesk.RemoteProvisioning.v1");
                if(row is null) {row=new RemoteProvisioning {DeviceId=id};db.RemoteProvisionings.Add(row);} else row.Id=Guid.NewGuid();
                row.ProtectedSettings=protector.Protect(result.Settings);row.ProtectedMeshId=protector.Protect(result.MeshId);row.Enabled=true;row.GroupRemoved=false;row.UnattendedAllowed=false;row.AccessPolicyVersion=checked(row.AccessPolicyVersion+1);
                db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action="remote.provision.approve",Target=id.ToString()});
                await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
            }
            catch(Exception e) when(e is not OperationCanceledException) {
                logs.CreateLogger("RemoteProvisioning").LogWarning("Provisioning unavailable: {Type}",e.GetType().Name);
                return Results.Problem("Remote provisioning unavailable",statusCode:503);
            }
        });
        admin.MapPut("/access-policy",async(Guid id,RemoteAccessPolicyRequest r,HttpContext c,AppDbContext db,PermissionService p,IMeshProvisioningClient mesh,IDataProtectionProvider protection,ILoggerFactory logs)=>{
            if(!await p.Allows(c.User.UserId(),"administration"))return Results.Forbid();
            await using var tx=await db.Database.BeginTransactionAsync();await DeviceOperationLock.Acquire(db,id);
            if(!await p.Allows(c.User.UserId(),"administration"))return Results.Forbid();
            var row=await db.RemoteProvisionings.SingleOrDefaultAsync(x=>x.DeviceId==id&&x.Enabled);
            if(row is null||!await db.DeviceCredentials.AnyAsync(x=>x.DeviceId==id&&x.RevokedAt==null))return Results.Conflict();
            if(row.AccessPolicyVersion!=r.ExpectedVersion)return Results.Conflict();
            if(row.UnattendedAllowed==r.UnattendedAllowed)return Results.NoContent();
            try {
                var meshId=protection.CreateProtector("OmniDesk.RemoteProvisioning.v1").Unprotect(row.ProtectedMeshId);
                await mesh.ApplySessionConsentPolicy(meshId,c.RequestAborted);
            } catch(Exception e)when(e is not OperationCanceledException){
                logs.CreateLogger("RemotePolicy").LogWarning("Remote policy unavailable: {Type}",e.GetType().Name);
                return Results.Problem("Remote policy unavailable",statusCode:503);
            }
            if(!await p.Allows(c.User.UserId(),"administration"))return Results.Forbid();
            row.UnattendedAllowed=r.UnattendedAllowed;row.AccessPolicyVersion=checked(row.AccessPolicyVersion+1);
            await db.RemoteSessions.Where(x=>x.DeviceId==id&&x.State=="active").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,"revocation_pending"));
            db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action=r.UnattendedAllowed?"remote.unattended.enable":"remote.unattended.disable",Target=id.ToString()});
            await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
        });
        admin.MapDelete("",async(Guid id,HttpContext c,AppDbContext db,PermissionService p)=>{
            if(!await p.Allows(c.User.UserId(),"administration"))return Results.Forbid();
            await using var tx=await db.Database.BeginTransactionAsync();await DeviceOperationLock.Acquire(db,id);
            if(!await p.Allows(c.User.UserId(),"administration"))return Results.Forbid();
            var changed=await db.RemoteProvisionings.Where(x=>x.DeviceId==id&&x.Enabled).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enabled,false));
            if(changed>0){
                await db.RemoteSessions.Where(x=>x.DeviceId==id&&x.State=="active").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,"revocation_pending"));
                db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action="remote.provision.disable",Target=id.ToString()});
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();return Results.NoContent();
        });
        var machine=app.MapGroup("/api/agent/remote").WithMetadata(new MachineRequest())
            .RequireAuthorization(new AuthorizeAttribute {AuthenticationSchemes=DeviceAuthentication.SchemeName});
        machine.MapGet("/configuration",async(HttpContext c,AppDbContext db,IDataProtectionProvider protection)=>{
            var device=Guid.Parse(c.User.UserId());
            var row=await db.RemoteProvisionings.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==device&&x.Enabled);
            if(row is null)return Results.NoContent();
            return Results.Ok(new RemoteAgentConfiguration(row.Id,protection.CreateProtector("OmniDesk.RemoteProvisioning.v1").Unprotect(row.ProtectedSettings),DateTimeOffset.UtcNow.AddSeconds(90)));
        });
        machine.MapPost("/node",async(RemoteNodeReport r,HttpContext c,AppDbContext db,IDataProtectionProvider protection,IMeshProvisioningClient mesh)=>{
            if(r.NodeId is null||!Regex.IsMatch(r.NodeId,@"^node//[A-Za-z0-9@$+=_-]{64}$"))return Results.BadRequest();
            var device=Guid.Parse(c.User.UserId());
            await using var tx=await db.Database.BeginTransactionAsync();await DeviceOperationLock.Acquire(db,device);
            if(!await db.DeviceCredentials.AnyAsync(x=>x.DeviceId==device&&x.RevokedAt==null))return Results.Unauthorized();
            var row=await db.RemoteProvisionings.SingleOrDefaultAsync(x=>x.DeviceId==device&&x.Id==r.ConfigurationId&&x.Enabled);
            if(row is null)return Results.Forbid();
            var meshId=protection.CreateProtector("OmniDesk.RemoteProvisioning.v1").Unprotect(row.ProtectedMeshId);
            if(!await mesh.ContainsNode(meshId,r.NodeId,c.RequestAborted))return Results.Conflict();
            if(await db.MeshDeviceMappings.AnyAsync(x=>x.NodeId==r.NodeId&&x.DeviceId!=device))return Results.Conflict();
            var mapping=await db.MeshDeviceMappings.FindAsync(device);
            if(mapping is not null&&mapping.NodeId!=r.NodeId)return Results.Conflict();
            if(mapping is null){
                db.MeshDeviceMappings.Add(new MeshDeviceMapping {DeviceId=device,NodeId=r.NodeId});
                db.AuditEvents.Add(new AuditEvent {Action="remote.node.bind",Target=device.ToString()});
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();return Results.NoContent();
        });
    }
}
