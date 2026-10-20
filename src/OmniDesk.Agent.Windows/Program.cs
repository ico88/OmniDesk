using System.Net.Http.Json;
using System.Security.Cryptography;
using OmniDesk.Agent.Windows;
using OmniDesk.Contracts;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("OmniDesk Agent requires Windows.");
if (args.Length == 3 && args[0] == "--apply-agent-update") { await AgentUpdater.Apply(args[1], args[2]); return; }
if (args.Length == 1 && args[0] == "self-test") { await WindowsSelfTest.Run(); return; }
if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("OmniDesk Agent: connect-test --server https://omnidesk.cricatania.it | enroll --server <HTTPS-origin> --token-file <protected-file> | enroll --server <HTTPS-origin> --token-stdin | revoke | self-test");
    return;
}
if (args.Length > 0 && args[0] == "connect-test")
{
    if (args.Length != 3 || args[1] != "--server") throw new ArgumentException("Usage: connect-test --server <HTTPS-origin>");
    try
    {
        var server = AgentConnection.ValidateServer(args[2]);
        using var client = AgentConnection.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await AgentConnection.CheckReady(client, server, timeout.Token);
        Console.WriteLine($"PASS: {server} HTTPS certificate verified and server ready. No enrollment or device operations performed.");
    }
    catch (Exception e) when (e is HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"Connection check failed ({e.GetType().Name}). Check server readiness, LAN routing, clock and CA trust in LocalMachine Root for the service account.");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Length > 0 && args[0] == "enroll")
{
    if (AgentIdentity.Load() is not null) throw new InvalidOperationException("Already enrolled. Revoke the old identity before enrolling again.");
    if (args.Length < 4 || args[1] != "--server" || args[3] is not ("--token-file" or "--token-stdin") ||
        (args[3] == "--token-file" && args.Length != 5) || (args[3] == "--token-stdin" && args.Length != 4))
        throw new ArgumentException("Usage: enroll --server <HTTPS-origin> --token-file <protected-file> | --token-stdin");
    var server = AgentIdentity.ValidateServer(args[2]);
    var token = (args[3] == "--token-stdin" ? await Console.In.ReadLineAsync() ?? "" : await File.ReadAllTextAsync(args[4])).Trim();
    if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid enrollment token.");
    AgentIdentity.SecureDirectory(AgentIdentity.Folder);
    using var rsa = RSA.Create(3072);
    using var client = AgentConnection.CreateClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await AgentConnection.CheckReady(client, server, timeout.Token);
    using var response = await client.PostAsJsonAsync(new Uri(server, "api/agent/enroll"), new EnrollmentRequest(token, Environment.MachineName, rsa.ExportSubjectPublicKeyInfoPem()), timeout.Token);
    response.EnsureSuccessStatusCode();
    var enrolled = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(timeout.Token) ?? throw new InvalidOperationException("Enrollment response missing.");
    AgentIdentity.Create(server.AbsoluteUri, enrolled.DeviceId, rsa).Save();
    Console.WriteLine($"Enrolled device {enrolled.DeviceId}.");
    return;
}
if (args.Length > 0 && args[0] == "revoke")
{
    if (args.Length != 1) throw new ArgumentException("Usage: revoke");
    var identity = AgentIdentity.Load();
    if (identity is null) return;
    using var client = AgentConnection.CreateClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(AgentIdentity.ValidateServer(identity.ServerUrl), "api/agent/uninstall"));
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", identity.Assertion());
    using var response = await client.SendAsync(request, timeout.Token);
    if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        throw new InvalidOperationException($"Server revocation failed (HTTP {(int)response.StatusCode}); uninstall cancelled.");
    File.Delete(AgentIdentity.ConfigPath);
    Console.WriteLine("Device identity revoked on server.");
    return;
}
if (args.Length > 0 && args[0] == "verify-enrollment")
{
    if (args.Length != 3 || args[1] != "--server") throw new ArgumentException("Usage: verify-enrollment --server <HTTPS-origin>");
    var expected = AgentConnection.ValidateServer(args[2]);
    var identity = AgentIdentity.Load() ?? throw new InvalidOperationException("Device is not enrolled.");
    if (AgentIdentity.ValidateServer(identity.ServerUrl) != expected) throw new InvalidOperationException("Enrollment belongs to another server.");
    _ = identity.Assertion(); // Verify protected key is readable without displaying it or sending it anywhere.
    Console.WriteLine($"PASS: protected enrollment for device {identity.DeviceId} and expected server. Server acceptance is verified by the next heartbeat.");
    return;
}
if (args.Length > 0) throw new ArgumentException("Unknown command. Use --help.");
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "OmniDeskAgent");
builder.Services.AddSingleton<AgentRuntimeState>();
builder.Services.AddHostedService<TrayPipeWorker>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<UpdateWorker>();
builder.Services.AddHostedService<RemoteAgentWorker>();
await builder.Build().RunAsync();
