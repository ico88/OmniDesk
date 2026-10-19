using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmniDesk.Contracts;
using Xunit;
namespace OmniDesk.Api.Tests;
public static class UpdateScenario
{
    public static async Task Run(HttpClient admin, HttpClient scoped, HttpClient agent, string scopedId, Func<string> assertion, Guid device, Guid group)
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

        var diagnostic = new DiagnosticRequest(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/devices/{device}/diagnostics/processes", diagnostic)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/devices/{device}/diagnostics/processes", diagnostic)).StatusCode);
        var servicesDiagnostic = new DiagnosticRequest(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/devices/{device}/diagnostics/services", servicesDiagnostic)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/devices/{device}/diagnostics/services", new DiagnosticRequest(Guid.NewGuid()))).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        var diagnosticClaim = await agent.PostAsync("/api/agent/jobs/claim", null);
        Assert.Equal(HttpStatusCode.OK, diagnosticClaim.StatusCode);
        var diagnosticLease = (await diagnosticClaim.Content.ReadFromJsonAsync<UpdateLease>())!;
        Assert.Equal("diagnostics.processes", diagnosticLease.Kind);
        var diagnosticResult = new UpdateResultRequest(diagnosticLease.LeaseId, true, false, [], DiagnosticJson: System.Text.Json.JsonSerializer.Serialize(new DiagnosticResult(
            "diagnostics.processes", DateTimeOffset.UtcNow, Processes: [new ProcessDiagnostic(12345, "notepad.exe", DateTimeOffset.UtcNow.AddSeconds(-10), true)])));
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{diagnosticLease.Id}/result", diagnosticResult)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        var servicesDiagnosticClaim = await agent.PostAsync("/api/agent/jobs/claim", null);
        Assert.Equal(HttpStatusCode.OK, servicesDiagnosticClaim.StatusCode);
        var servicesDiagnosticLease = (await servicesDiagnosticClaim.Content.ReadFromJsonAsync<UpdateLease>())!;
        Assert.Equal("diagnostics.services", servicesDiagnosticLease.Kind);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        var servicesResult = new UpdateResultRequest(servicesDiagnosticLease.LeaseId, true, false, [], DiagnosticJson: System.Text.Json.JsonSerializer.Serialize(new DiagnosticResult(
            "diagnostics.services", DateTimeOffset.UtcNow, Services: [new ServiceDiagnostic("Spooler", "Print Spooler", "Running", "Automatic")])));
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{servicesDiagnosticLease.Id}/result", servicesResult)).StatusCode);
        var diagnostics = await admin.GetFromJsonAsync<JsonElement[]>($"/api/devices/{device}/diagnostics");
        Assert.Equal(2, diagnostics!.Length);

        var serviceAction = new ServiceControlRequest(Guid.NewGuid(), "Spooler", "stop");
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/devices/{device}/services/actions", serviceAction)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/services/actions", serviceAction with { ServiceName = "../../cmd" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/services/actions", serviceAction with { ServiceName = "OmniDeskAgent" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Service Technician", permissions = new[] { "services.manage" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Service Technician", groupId = group, deviceId = (Guid?)null })).StatusCode);
        var actionQueued = await scoped.PostAsJsonAsync($"/api/devices/{device}/services/actions", serviceAction);
        Assert.Equal(HttpStatusCode.Created, actionQueued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scoped.PostAsJsonAsync($"/api/devices/{device}/services/actions", serviceAction)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        var serviceClaim = await agent.PostAsync("/api/agent/jobs/claim", null);
        Assert.Equal(HttpStatusCode.OK, serviceClaim.StatusCode);
        var serviceLease = (await serviceClaim.Content.ReadFromJsonAsync<UpdateLease>())!;
        Assert.Equal("service.stop", serviceLease.Kind);
        Assert.Equal("Spooler", serviceLease.ServiceControl!.ServiceName);
        Assert.Equal("stop", serviceLease.ServiceControl.Action);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{serviceLease.Id}/result", new UpdateResultRequest(serviceLease.LeaseId, true, false, []))).StatusCode);
        Assert.Single(await admin.GetFromJsonAsync<JsonElement[]>($"/api/devices/{device}/services/jobs"));

        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        var processAction = new ProcessControlRequest(Guid.NewGuid(), 12345, "notepad.exe", startedAt);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction with { ProcessId = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction with { ProcessName = "lsass.exe" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction with { ProcessName = "MeshService64.exe" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction with { StartedAt = default })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Process Technician", permissions = new[] { "processes.manage" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Process Technician", groupId = group, deviceId = (Guid?)null })).StatusCode);
        var queuedProcess = await scoped.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction);
        Assert.Equal(HttpStatusCode.Created, queuedProcess.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scoped.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction)).StatusCode);
        var queuedProcessId = (await queuedProcess.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"/api/devices/{device}/processes/jobs/{queuedProcessId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scoped.DeleteAsync($"/api/devices/{device}/processes/jobs/{queuedProcessId}")).StatusCode);
        processAction = processAction with { RequestId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.Created, (await scoped.PostAsJsonAsync($"/api/devices/{device}/processes/actions", processAction)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        var processClaim = await agent.PostAsync("/api/agent/jobs/claim", null);
        Assert.Equal(HttpStatusCode.OK, processClaim.StatusCode);
        var processLease = (await processClaim.Content.ReadFromJsonAsync<UpdateLease>())!;
        Assert.Equal("process.kill", processLease.Kind);
        Assert.Equal(12345, processLease.ProcessControl!.ProcessId);
        Assert.Equal(startedAt, processLease.ProcessControl.StartedAt);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{processLease.Id}/result", new UpdateResultRequest(processLease.LeaseId, true, false, []))).StatusCode);
        var processJobs = await scoped.GetFromJsonAsync<JsonElement[]>($"/api/devices/{device}/processes/jobs");
        Assert.Equal(2, processJobs!.Length);
    }
}
