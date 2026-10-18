using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OmniDesk.Agent.Windows;
public sealed class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var collector = new WindowsCollector(); var inventoryAt = DateTimeOffset.MinValue; var delay = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var identity = AgentIdentity.Load();
                if (identity is null) { logger.LogInformation("Not enrolled. Run the elevated enrollment command."); }
                else
                {
                    var server = AgentIdentity.ValidateServer(identity.ServerUrl);
                    async Task Send(string path, HttpMethod method, object payload)
                    {
                        using var request = new HttpRequestMessage(method, new Uri(server, path)) { Content = JsonContent.Create(payload, payload.GetType()) };
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Assertion());
                        using var response = await client.SendAsync(request, stoppingToken);
                        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                            throw new DeviceRevokedException();
                        response.EnsureSuccessStatusCode();
                    }
                    await Send("api/agent/metrics", HttpMethod.Post, collector.Metrics());
                    if (DateTimeOffset.UtcNow - inventoryAt > TimeSpan.FromHours(24))
                    { await Send("api/agent/inventory", HttpMethod.Put, collector.Inventory()); inventoryAt = DateTimeOffset.UtcNow; }
                }
                delay = 60;
            }
            catch (DeviceRevokedException) { logger.LogError("Device authentication rejected. Re-enrollment requires administrator action. Worker stopped."); return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning("Collection/upload failed ({Type}); retry with backoff.", e.GetType().Name); delay = Math.Min(delay * 2, 900); }
            try { await Task.Delay(TimeSpan.FromSeconds(delay + Random.Shared.Next(0, 10)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
    private sealed class DeviceRevokedException : Exception;
}
