using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmniDesk.Contracts;
using Xunit;
namespace OmniDesk.Api.Tests;
public static class UpdateScenario
{
    public static async Task Run(HttpClient admin, HttpClient scoped, HttpClient agent, Func<string> assertion, Guid device)
    {
        var request = new UpdateJobRequest(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/devices/{device}/updates/scan", request)).StatusCode);
        var queued = await admin.PostAsJsonAsync($"/api/devices/{device}/updates/scan", request);
        Assert.Equal(HttpStatusCode.Created, queued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/devices/{device}/updates/scan", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/devices/{device}/updates/scan", new UpdateJobRequest(Guid.NewGuid()))).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        var claimed = await agent.PostAsync("/api/agent/jobs/claim", null);
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
        var lease = (await claimed.Content.ReadFromJsonAsync<UpdateLease>())!;
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsync("/api/agent/jobs/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/devices/{device}/updates/{lease.Id}")).StatusCode);
        var updateId = Guid.NewGuid();
        var result = new UpdateResultRequest(lease.LeaseId, true, false, [new(updateId, 1, "Fixture update", ["123"], false)]);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NotFound, (await agent.PostAsJsonAsync($"/api/agent/jobs/{lease.Id}/result", result with { LeaseId = Guid.NewGuid() })).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{lease.Id}/result", result)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{lease.Id}/result", result)).StatusCode);
        var install = new UpdateJobRequest(Guid.NewGuid(), [new(updateId, 1)]);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/devices/{device}/updates/install", install)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/devices/{device}/updates/install", install)).StatusCode);
        var jobs = await admin.GetFromJsonAsync<JsonElement[]>($"/api/devices/{device}/updates");
        Assert.Equal(2, jobs!.Length);
        var installId = jobs.Single(x => x.GetProperty("kind").GetString() == "install").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/devices/{device}/updates/{installId}")).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsync("/api/agent/jobs/claim", null)).StatusCode);
    }
}
