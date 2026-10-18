using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniDesk.Remote.MeshCentral;
using Xunit;
namespace OmniDesk.Api.Tests;
public sealed class FakeMesh : IMeshCentralClient
{
    public bool Configured => true;
    public List<string> Calls { get; } = [];
    public List<string> RevokedNodes { get; } = [];
    public Task<MeshSession> Create(string node, string capability, string actor, CancellationToken ct)
    { Calls.Add(capability); return Task.FromResult(new MeshSession(Guid.NewGuid().ToString(), "https://mesh.example.test/sharing?c=fixture", DateTimeOffset.UtcNow.AddMinutes(5))); }
    public Task Revoke(string node, string publicId, CancellationToken ct) { Calls.Add("revoke"); RevokedNodes.Add(node); return Task.CompletedTask; }
}
public static class RemoteScenario
{
    public static async Task Run(HttpClient admin, HttpClient viewer, HttpClient scoped, Guid device, FakeMesh mesh)
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
        }
        Assert.Contains("remote.control", mesh.Calls); Assert.Contains("terminal", mesh.Calls); Assert.Contains("files", mesh.Calls);
        Assert.All(mesh.RevokedNodes, node => Assert.Equal("node//abcdefghijk", node));
        Assert.Equal(3, mesh.Calls.Count(x => x == "revoke"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "administration" })).StatusCode);
    }
}
