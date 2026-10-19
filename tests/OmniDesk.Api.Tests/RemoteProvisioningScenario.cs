using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
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
        Assert.Equal(HttpStatusCode.NoContent,(await admin.DeleteAsync(route)).StatusCode);
        Auth();Assert.Equal(HttpStatusCode.NoContent,(await agent.GetAsync("/api/agent/remote/configuration")).StatusCode);
        Auth();Assert.Equal(HttpStatusCode.Forbidden,(await agent.PostAsJsonAsync("/api/agent/remote/node",report)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions",new {capability="remote.control"})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PostAsync(route,null)).StatusCode);
    }
}
