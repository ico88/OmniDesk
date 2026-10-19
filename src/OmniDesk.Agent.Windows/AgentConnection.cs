using System.Net;
using System.Text.Json;

namespace OmniDesk.Agent.Windows;

public static class AgentConnection
{
    public static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public static Uri ValidateServer(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("Server must be an HTTPS origin without credentials, path, query or fragment.");
        return uri;
    }

    public static async Task CheckReady(HttpClient client, Uri server, CancellationToken ct = default)
    {
        server = ValidateServer(server.AbsoluteUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "api/health/ready"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"Server readiness failed (HTTP {(int)response.StatusCode}); redirects are not accepted.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > 16384) throw new InvalidOperationException("Server readiness response too large.");
            bytes.Write(buffer, 0, read);
        }
        using var json = JsonDocument.Parse(bytes.ToArray());
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || status.GetString() != "ready") throw new InvalidOperationException("Server did not report ready.");
    }
}
