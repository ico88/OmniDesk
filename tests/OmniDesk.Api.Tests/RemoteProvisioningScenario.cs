using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using Microsoft.Extensions.Logging.Abstractions;
using OmniDesk.Contracts;
using Xunit;
namespace OmniDesk.Api.Tests;

public static class RemoteProvisioningScenario
{
    public static async Task Run(ApiFactory factory,HttpClient admin,HttpClient scoped,HttpClient agent,Func<string> assertion,Guid device)
    {
        var route=$"/api/admin/devices/{device}/remote-provisioning";
        void Auth()=>agent.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",assertion());
        Auth();Assert.Equal(HttpStatusCode.NoContent,(await agent.GetAsync("/api/agent/remote/configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.PostAsync(route,null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PostAsync(route,null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PostAsync(route,null)).StatusCode);
        using(var scope=factory.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var row=await db.RemoteProvisionings.FindAsync(device);
            Assert.NotNull(row);Assert.DoesNotContain("encrypted-fixture",row.ProtectedSettings);Assert.DoesNotContain("fixture",row.ProtectedMeshId);
        }
        Assert.Equal(HttpStatusCode.Unauthorized,(await admin.GetAsync("/api/agent/remote/configuration")).StatusCode);
        Auth();var config=(await agent.GetFromJsonAsync<RemoteAgentConfiguration>("/api/agent/remote/configuration"))!;
        Assert.Equal("MeshName=encrypted-fixture",config.Settings);Assert.InRange(config.AuthorizedUntil-DateTimeOffset.UtcNow,TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(91));
        var report=new RemoteNodeReport(config.Id,"node//"+new string('Q',64));
        Auth();Assert.Equal(HttpStatusCode.Conflict,(await agent.PostAsJsonAsync("/api/agent/remote/node",report)).StatusCode);
        factory.Mesh.ValidNode=true;
        Auth();Assert.Equal(HttpStatusCode.Forbidden,(await agent.PostAsJsonAsync("/api/agent/remote/node",report with {ConfigurationId=Guid.NewGuid()})).StatusCode);
        Auth();Assert.Equal(HttpStatusCode.NoContent,(await agent.PostAsJsonAsync("/api/agent/remote/node",report)).StatusCode);
        Auth();Assert.Equal(HttpStatusCode.NoContent,(await agent.PostAsJsonAsync("/api/agent/remote/node",report)).StatusCode);
        var availability=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/devices/{device}/remote-sessions/availability"));
        Assert.True(availability.GetProperty("enabled").GetBoolean());
        var policyRoute=route+"/access-policy";
        async Task<long> Version()=> (await admin.GetFromJsonAsync<System.Text.Json.JsonElement>(route)).GetProperty("accessPolicyVersion").GetInt64();
        var version=await Version();
        Assert.False((await admin.GetFromJsonAsync<System.Text.Json.JsonElement>(route)).GetProperty("unattendedAllowed").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.PostAsync(route+"/refresh",null)).StatusCode);
        var sessionResponse=await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions",new {capability="remote.control"});
        Assert.Equal(HttpStatusCode.Created,sessionResponse.StatusCode);
        var refreshSession=(await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        var oldConfigurationId=config.Id;
        factory.Mesh.ProvisionedSettings="MeshName=refreshed-fixture";
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PostAsync(route+"/refresh",null)).StatusCode);
        Assert.Equal(2,factory.Mesh.ProvisionCalls);
        Auth();var refreshedConfig=(await agent.GetFromJsonAsync<RemoteAgentConfiguration>("/api/agent/remote/configuration"))!;
        Assert.NotEqual(oldConfigurationId,refreshedConfig.Id);
        Assert.Equal("MeshName=refreshed-fixture",refreshedConfig.Settings);
        var refreshedSession=await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/devices/{device}/remote-sessions");
        Assert.Equal("revocation_pending",refreshedSession.EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==refreshSession).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await admin.DeleteAsync($"/api/devices/{device}/remote-sessions/{refreshSession}")).StatusCode);
        version=await Version();
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.PutAsJsonAsync(policyRoute,new {unattendedAllowed=true,expectedVersion=version})).StatusCode);
        factory.Mesh.FailPolicy=true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await admin.PutAsJsonAsync(policyRoute,new {unattendedAllowed=true,expectedVersion=version})).StatusCode);
        Assert.Equal(version,await Version());factory.Mesh.FailPolicy=false;
        Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions",new {capability="remote.control",consentRequired=false})).StatusCode);
        Assert.True(factory.Mesh.ConsentRequests.Last());
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync(policyRoute,new {unattendedAllowed=true,expectedVersion=version})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync(policyRoute,new {unattendedAllowed=false,expectedVersion=version})).StatusCode);
        version=await Version();
        await new RemoteSessionGuard(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<RemoteSessionGuard>.Instance).Sweep(CancellationToken.None);
        foreach(var capability in new[]{"remote.control","terminal","files"}) {
            Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions",new {capability})).StatusCode);
            Assert.False(factory.Mesh.ConsentRequests.Last());
        }
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync(policyRoute,new {unattendedAllowed=false,expectedVersion=version})).StatusCode);
        version=await Version();
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync(policyRoute,new {unattendedAllowed=true,expectedVersion=version})).StatusCode);
        await new RemoteSessionGuard(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<RemoteSessionGuard>.Instance).Sweep(CancellationToken.None);
        var inflightVersion=await Version();
        factory.Mesh.DuringCreate=async()=>Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync(policyRoute,new {unattendedAllowed=false,expectedVersion=inflightVersion})).StatusCode);
        var changedDuringCreation=await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions",new {capability="remote.control"});
        Assert.Equal(HttpStatusCode.Forbidden,changedDuringCreation.StatusCode);
        var sessionGuard=new RemoteSessionGuard(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<RemoteSessionGuard>.Instance);
        await sessionGuard.Sweep(CancellationToken.None);
        using(var scope=factory.Services.CreateScope()) {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.RemoteSessions.AnyAsync(x=>x.DeviceId==device&&(x.State=="active"||x.State=="revocation_pending")));
            Assert.True(await db.AuditEvents.AnyAsync(x=>x.Action=="remote.unattended.enable"&&x.Target==device.ToString()));
            Assert.True(await db.AuditEvents.AnyAsync(x=>x.Action=="remote.unattended.disable"&&x.Target==device.ToString()));
            Assert.True(await db.AuditEvents.AnyAsync(x=>x.Action=="remote.provision.refresh"&&x.Target==device.ToString()));
        }
        Assert.Equal(HttpStatusCode.NoContent,(await admin.DeleteAsync(route)).StatusCode);
        availability=await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/devices/{device}/remote-sessions/availability");
        Assert.False(availability.GetProperty("enabled").GetBoolean());
        var guard=new RemoteProvisioningGuard(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<RemoteProvisioningGuard>.Instance);
        await guard.Sweep(CancellationToken.None);
        using(var scope=factory.Services.CreateScope()) {
            var row=await scope.ServiceProvider.GetRequiredService<AppDbContext>().RemoteProvisionings.FindAsync(device);
            Assert.True(row!.GroupRemoved);Assert.Empty(row.ProtectedSettings);
        }
        var count=factory.Mesh.GroupsRemoved;Assert.True(count>0);await guard.Sweep(CancellationToken.None);Assert.Equal(count,factory.Mesh.GroupsRemoved);
        Auth();Assert.Equal(HttpStatusCode.NoContent,(await agent.GetAsync("/api/agent/remote/configuration")).StatusCode);
        Auth();Assert.Equal(HttpStatusCode.Forbidden,(await agent.PostAsJsonAsync("/api/agent/remote/node",report)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions",new {capability="remote.control"})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PostAsync(route,null)).StatusCode);
    }
}
