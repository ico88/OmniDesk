using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmniDesk.Contracts;
namespace OmniDesk.Agent.Windows;
public sealed class UpdateWorker(ILogger<UpdateWorker> logger) : BackgroundService
{
    private sealed record Receipt(Guid JobId, Guid LeaseId, UpdateResultRequest? Result);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = AgentConnection.CreateClient();
        var receiptPath = Path.Combine(AgentIdentity.Folder, "update-receipt.json");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var identity = AgentIdentity.Load();
                if (identity is not null)
                {
                    var server = AgentIdentity.ValidateServer(identity.ServerUrl);
                    async Task<HttpResponseMessage> Send(string path, object? payload)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, path));
                        if (payload is not null) request.Content = JsonContent.Create(payload, payload.GetType());
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Assertion());
                        return await client.SendAsync(request, stoppingToken);
                    }
                    if (File.Exists(receiptPath)) AgentIdentity.AssertTrusted(new FileInfo(receiptPath));
                    Receipt? receipt = File.Exists(receiptPath) ? JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath, stoppingToken), Json) : null;
                    if (receipt is not null && receipt.Result is null)
                    {
                        // Crash during native operation: never execute the same install again automatically.
                        receipt = receipt with { Result = new(receipt.LeaseId, false, false, [], "Operation interrupted; inspect Windows Update history before retry") };
                        Save(receiptPath, receipt);
                    }
                    if (receipt is not null)
                    {
                        using var ack = await Send($"api/agent/jobs/{receipt.JobId}/result", receipt.Result);
                        if (ack.IsSuccessStatusCode || ack.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound) File.Delete(receiptPath);
                        else ack.EnsureSuccessStatusCode();
                    }
                    else
                    {
                        using var response = await Send("api/agent/jobs/claim", null);
                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { logger.LogError("Update worker authentication rejected; stopped."); return; }
                        response.EnsureSuccessStatusCode();
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var job = await response.Content.ReadFromJsonAsync<UpdateLease>(cancellationToken: stoppingToken) ?? throw new InvalidOperationException("Missing lease");
                            Save(receiptPath, new Receipt(job.Id, job.LeaseId, null));
                            var result = await Execute(job, stoppingToken);
                            Save(receiptPath, new Receipt(job.Id, job.LeaseId, result));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning("Update worker failed: {Type}; no automatic job re-execution.", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(20 + Random.Shared.Next(0, 10)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
    private static void Save(string path, Receipt value)
    { AgentIdentity.WriteTrusted(path, JsonSerializer.Serialize(value)); }
    private static async Task<UpdateResultRequest> Execute(UpdateLease job, CancellationToken ct)
    {
        var scriptPath = Path.Combine(AgentIdentity.Folder, "update-worker.ps1");
        using var resource = typeof(UpdateWorker).Assembly.GetManifestResourceStream("OmniDesk.Agent.Windows.update-worker.ps1") ?? throw new InvalidOperationException("Missing embedded update script");
        using var reader = new StreamReader(resource); AgentIdentity.WriteTrusted(scriptPath, await reader.ReadToEndAsync(ct));
        var input = Path.Combine(AgentIdentity.Folder, "update-input.json"); AgentIdentity.WriteTrusted(input, JsonSerializer.Serialize(job));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Process-only policy for the fixed embedded local script. Group Policy still takes precedence.
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", scriptPath }) start.ArgumentList.Add(arg);
        start.Environment["OMNIDESK_UPDATE_INPUT"] = input;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Windows Update worker");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(60));
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token); var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree:true); throw; }
        var output = await stdout; var errorOutput = await stderr;
        if (process.ExitCode != 0 || output.Length > 1024 * 1024) return new(job.LeaseId, false, false, [], process.ExitCode != 0 ? $"Windows Update worker exit {process.ExitCode}" + (errorOutput.Contains("PSSecurityException",StringComparison.OrdinalIgnoreCase) ? ": PowerShell execution policy denied" : "") : "Windows Update result exceeds maximum size");
        var result = JsonSerializer.Deserialize<UpdateResultRequest>(output, Json) ?? throw new InvalidOperationException("Invalid Windows Update output");
        if (result.LeaseId != job.LeaseId) throw new InvalidOperationException("Lease mismatch");
        return result;
    }
}
