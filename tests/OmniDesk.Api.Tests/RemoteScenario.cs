using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniDesk.Api.Security;
using OmniDesk.Remote.MeshCentral;
using Xunit;
namespace OmniDesk.Api.Tests;
public sealed class FakeMesh : IMeshCentralClient, IMeshProvisioningClient
{
    public bool Configured => true;
    public List<string> Calls { get; } = [];
    public List<string> RevokedNodes { get; } = [];
    public List<string> DisconnectedShares { get; } = [];
    public int GroupsRemoved { get; private set; }
    public List<bool> ConsentRequests {get;}=[];
    public List<string> OperatorNames { get; } = [];
    public bool FailPolicy {get;set;}
    public string ProvisionedSettings { get; set; } = "MeshName=encrypted-fixture";
    public int ProvisionCalls { get; private set; }
    public Task ApplySessionConsentPolicy(string meshId,CancellationToken ct) { if(FailPolicy)throw new InvalidOperationException("Fixture policy failure");return Task.CompletedTask; }
    public Task CheckReady(CancellationToken ct)=>Task.CompletedTask;
    public Task RemoveGroup(string meshId,CancellationToken ct){GroupsRemoved++;return Task.CompletedTask;}
    public bool ValidNode { get; set; }
    public Task<MeshAgentProvisioning> Provision(Guid deviceId,CancellationToken ct) { ProvisionCalls++; return Task.FromResult(new MeshAgentProvisioning("mesh//fixture", ProvisionedSettings)); }
    public Task<bool> ContainsNode(string meshId,string nodeId,CancellationToken ct) => Task.FromResult(ValidNode);
    public Func<Task>? DuringCreate {get;set;}
    public bool FailRevocation { get; set; }
    public bool FailDisconnect { get; set; }
    public async Task<MeshSession> Create(string node, string capability, string actor, bool consentRequired, CancellationToken ct)
    { ConsentRequests.Add(consentRequired); OperatorNames.Add(actor); Calls.Add(capability); var callback=DuringCreate;DuringCreate=null;if(callback is not null)await callback();return new MeshSession(Guid.NewGuid().ToString(), "https://mesh.example.test/sharing?c=fixture", DateTimeOffset.UtcNow.AddMinutes(5)); }
    public Task Revoke(string node, string publicId, CancellationToken ct) { if (FailRevocation) throw new InvalidOperationException("Fixture revocation failure"); Calls.Add("revoke"); RevokedNodes.Add(node); return Task.CompletedTask; }
    public Task Disconnect(string node, string publicId, CancellationToken ct) { if (FailDisconnect) throw new InvalidOperationException("Fixture disconnect failure"); Calls.Add("disconnect"); DisconnectedShares.Add(publicId); return Task.CompletedTask; }
}
public static class RemoteScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient viewer, HttpClient scoped, Guid device, FakeMesh mesh)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}/mesh", new { nodeId = "node//abcdefghijk" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" })).StatusCode);
        Assert.Empty(mesh.Calls);
        foreach (var capability in new[] { "remote.control", "terminal", "files" })
        {
            var response = await scoped.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/devices/{device}/remote-sessions/{body.GetProperty("id").GetGuid()}")).StatusCode);
            if (capability == "files") Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}/mesh", new { nodeId = "node//differentnode" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"/api/devices/{device}/remote-sessions/{body.GetProperty("id").GetGuid()}")).StatusCode);
            var endedSessions = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{device}/remote-sessions");
            Assert.NotEqual(JsonValueKind.Null, endedSessions.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == body.GetProperty("id").GetGuid()).GetProperty("endedAt").ValueKind);
        }
        Assert.Contains("remote.control", mesh.Calls); Assert.Contains("terminal", mesh.Calls); Assert.Contains("files", mesh.Calls);
        Assert.All(mesh.OperatorNames, name => Assert.Equal("Federico Operatore", name));
        Assert.All(mesh.RevokedNodes, node => Assert.Equal("node//abcdefghijk", node));
        Assert.Equal(3, mesh.Calls.Count(x => x == "revoke"));
        var disconnectResponse = await scoped.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" });
        Assert.Equal(HttpStatusCode.Created, disconnectResponse.StatusCode);
        var disconnectSession = (await disconnectResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/devices/{device}/remote-sessions/{disconnectSession}/disconnect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await scoped.PostAsync($"/api/devices/{device}/remote-sessions/{disconnectSession}/disconnect", null)).StatusCode);
        Assert.Equal(2, mesh.DisconnectedShares.Count);
        var stillActive = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{device}/remote-sessions");
        var activeSession = stillActive.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == disconnectSession);
        Assert.Equal("active", activeSession.GetProperty("state").GetString());
        Assert.Equal("Federico Operatore", activeSession.GetProperty("operatorName").GetString());
        var disconnectCount = mesh.Calls.Count(x => x == "disconnect");
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"/api/devices/{device}/remote-sessions/{disconnectSession}")).StatusCode);
        Assert.Equal(disconnectCount + 1, mesh.Calls.Count(x => x == "disconnect"));
        Assert.Equal(new[] { "disconnect", "revoke" }, mesh.Calls.TakeLast(2));
        var pendingResponse = await scoped.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" });
        Assert.Equal(HttpStatusCode.Created, pendingResponse.StatusCode);
        var pending = (await pendingResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        mesh.FailDisconnect = true;
        Assert.Equal(HttpStatusCode.Accepted, (await scoped.DeleteAsync($"/api/devices/{device}/remote-sessions/{pending}")).StatusCode);
        var sessions = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{device}/remote-sessions");
        Assert.Equal("revocation_pending", sessions.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == pending).GetProperty("state").GetString());
        var blockedReconnect = await scoped.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" });
        Assert.Equal(HttpStatusCode.Conflict, blockedReconnect.StatusCode);
        Assert.Equal("remote_revocation_pending", (await blockedReconnect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var guard = new RemoteSessionGuard(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RemoteSessionGuard>.Instance);
        await guard.Sweep(CancellationToken.None);
        mesh.FailDisconnect = false;
        mesh.FailRevocation = true;
        await guard.Sweep(CancellationToken.None);
        sessions = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{device}/remote-sessions");
        Assert.Equal("revocation_pending", sessions.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == pending).GetProperty("state").GetString());
        mesh.FailRevocation = false;
        var successfulRetryIndex = mesh.Calls.Count;
        await guard.Sweep(CancellationToken.None);
        Assert.Equal(new[] { "disconnect", "revoke" }, mesh.Calls.Skip(successfulRetryIndex).TakeLast(2));
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"/api/devices/{device}/remote-sessions/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"/api/devices/{device}/remote-sessions/{pending}")).StatusCode);
        var afterRevocation = await scoped.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" });
        Assert.Equal(HttpStatusCode.Created, afterRevocation.StatusCode);
        var afterRevocationId = (await afterRevocation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"/api/devices/{device}/remote-sessions/{afterRevocationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "administration" })).StatusCode);
    }
}
