using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Management;
using OmniDesk.Contracts;
namespace OmniDesk.Agent.Windows;
internal sealed record UpdateReceipt(Guid JobId, Guid LeaseId, UpdateResultRequest? Result, string? Kind = null,
    string? TargetVersion = null, string? MsiSha256 = null, string? State = null, DateTimeOffset? StartedAt = null);
public sealed class UpdateWorker(ILogger<UpdateWorker> logger) : BackgroundService
{
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
                    UpdateReceipt? receipt = File.Exists(receiptPath) ? JsonSerializer.Deserialize<UpdateReceipt>(await File.ReadAllTextAsync(receiptPath, stoppingToken), Json) : null;
                    if (receipt is not null && receipt.Result is null)
                    {
                        if (receipt.Kind == "agent.update" && receipt.State == "installing")
                        {
                            if (receipt.StartedAt < DateTimeOffset.UtcNow.AddMinutes(-15) && !Process.GetProcessesByName("OmniDeskUpdater").Any())
                            {
                                var installed = Version.TryParse(AgentVersion(), out var parsed) ? parsed : new Version(0, 0);
                                var target = Version.TryParse(receipt.TargetVersion, out var expected) ? expected : new Version(int.MaxValue, 0);
                                var succeeded = installed >= target;
                                receipt = receipt with { State = succeeded ? "installed" : "failed", Result = new(receipt.LeaseId, succeeded, false, [], succeeded ? null : "MSI update did not complete; inspect Windows Installer log before retry") }; Save(receiptPath, receipt);
                            }
                        }
                        else
                        {
                            // Crash during native operation: never execute the same install again automatically.
                            receipt = receipt with { Result = new(receipt.LeaseId, false, false, [], "Operation interrupted; inspect Windows Update history before retry") };
                            Save(receiptPath, receipt);
                        }
                    }
                    if (receipt?.Result is not null)
                    {
                        using var ack = await Send($"api/agent/jobs/{receipt.JobId}/result", receipt.Result);
                        if (ack.IsSuccessStatusCode || ack.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                        {
                            File.Delete(receiptPath);
                            if (receipt.Kind == "agent.update")
                            {
                                var staged = Path.Combine(AgentIdentity.Folder, $"agent-update-{receipt.JobId:N}.msi");
                                if (File.Exists(staged)) File.Delete(staged);
                                var updater = Path.Combine(AgentIdentity.Folder, "OmniDeskUpdater.exe");
                                if (File.Exists(updater)) File.Delete(updater);
                                if (receipt.Result.Succeeded) { var log = Path.Combine(AgentIdentity.Folder, $"agent-update-{receipt.JobId:N}.log"); if (File.Exists(log)) File.Delete(log); }
                            }
                        }
                        else ack.EnsureSuccessStatusCode();
                    }
                    else if (receipt is null)
                    {
                        using var response = await Send("api/agent/jobs/claim", null);
                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { logger.LogError("Update worker authentication rejected; stopped."); return; }
                        response.EnsureSuccessStatusCode();
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var job = await response.Content.ReadFromJsonAsync<UpdateLease>(cancellationToken: stoppingToken) ?? throw new InvalidOperationException("Missing lease");
                            if (job.Kind == "agent.update")
                            {
                                UpdateResultRequest? outcome;
                                try { outcome = await BeginAgentUpdate(job, client, server, identity.Assertion(), receiptPath, stoppingToken); }
                                catch (Exception e) when (e is HttpRequestException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or IOException or Win32Exception)
                                { outcome = new(job.LeaseId, false, false, [], $"Agent update staging failed ({e.GetType().Name})"); }
                                if (outcome is not null) Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, outcome, "agent.update"));
                                else logger.LogInformation("Agent MSI update launched; the protected receipt will be reported when Windows Installer completes.");
                            }
                            else if (job.Kind is "diagnostics.processes" or "diagnostics.services" or "diagnostics.events")
                            {
                                try
                                {
                                    var diagnostic = DiagnosticCollector.Collect(job.Kind);
                                    var result = new UpdateResultRequest(job.LeaseId, true, false, [], DiagnosticJson: JsonSerializer.Serialize(diagnostic));
                                    Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, result, job.Kind));
                                }
                                catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
                                {
                                    var result = new UpdateResultRequest(job.LeaseId, false, false, [], $"Diagnostic collection failed ({e.GetType().Name})");
                                    Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, result, job.Kind));
                                }
                            }
                            else if (job.Kind is "scan" or "install")
                            {
                                Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, null));
                                var result = await Execute(job, stoppingToken);
                                Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, result));
                            }
                            else
                            {
                                var result = new UpdateResultRequest(job.LeaseId, false, false, [], "Unsupported job kind");
                                Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, result, job.Kind));
                            }
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
    internal static void Save(string path, UpdateReceipt value)
    { AgentIdentity.WriteTrusted(path, JsonSerializer.Serialize(value)); }
    private static string AgentVersion() => typeof(UpdateWorker).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    private static async Task<UpdateResultRequest?> BeginAgentUpdate(UpdateLease job, HttpClient client, Uri server, string assertion, string receiptPath, CancellationToken ct)
    {
        var package = job.AgentUpdate ?? throw new InvalidOperationException("Update package metadata missing");
        if (!Version.TryParse(package.Version, out var target) || target <= Version.Parse(AgentVersion()) || package.Size is < 1 or > 268435456 ||
            package.Sha256.Length != 64 || package.Sha256.Any(x => !Uri.IsHexDigit(x))) throw new InvalidOperationException("Invalid update package metadata");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, $"api/agent/jobs/{job.Id}/package?leaseId={job.LeaseId}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength != package.Size) throw new InvalidDataException("MSI package size mismatch");
        var msiPath = Path.Combine(AgentIdentity.Folder, $"agent-update-{job.Id:N}.msi");
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var targetFile = new FileStream(msiPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            await source.CopyToAsync(targetFile, ct);
            if (targetFile.Length != package.Size) throw new InvalidDataException("MSI package size mismatch");
        }
        AgentIdentity.ProtectTrustedFile(msiPath);
        await using (var verify = File.OpenRead(msiPath))
            if (Convert.ToHexString(await SHA256.HashDataAsync(verify, ct)).ToLowerInvariant() != package.Sha256) throw new InvalidDataException("MSI package hash mismatch");
        AgentIdentity.AssertTrusted(new FileInfo(msiPath));
        var updaterPath = Path.Combine(AgentIdentity.Folder, "OmniDeskUpdater.exe");
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Agent executable path unavailable");
        File.Copy(self, updaterPath, true); AgentIdentity.AssertTrusted(new FileInfo(updaterPath));
        Save(receiptPath, new UpdateReceipt(job.Id, job.LeaseId, null, "agent.update", package.Version, package.Sha256, "installing", DateTimeOffset.UtcNow));
        var start = new ProcessStartInfo(updaterPath) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-agent-update"); start.ArgumentList.Add(receiptPath); start.ArgumentList.Add(msiPath);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Cannot start protected updater");
        return null;
    }
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
