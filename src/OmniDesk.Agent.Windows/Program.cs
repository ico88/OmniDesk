using System.Net.Http.Json;
using System.Security.Cryptography;
using OmniDesk.Agent.Windows;
using OmniDesk.Contracts;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("OmniDesk Agent requires Windows.");
if (args.Length == 1 && args[0] == "self-test") { await WindowsSelfTest.Run(); return; }
if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("OmniDesk Agent: connect-test --server https://192.168.1.233:8085 | enroll --server <HTTPS-origin> --token-file <protected-file> | self-test");
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
    string Option(string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {key}"); }
    if (AgentIdentity.Load() is not null) throw new InvalidOperationException("Already enrolled. Revoke the old identity and remove its local configuration before enrolling again.");
    var server = AgentIdentity.ValidateServer(Option("--server"));
    var tokenPath = Option("--token-file");
    var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
    if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid enrollment token file.");
    // Establish ACLs before contacting server, so an unprivileged enrollment cannot consume a token.
    AgentIdentity.SecureDirectory(AgentIdentity.Folder);
    using var rsa = RSA.Create(3072);
    using var client = AgentConnection.CreateClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await AgentConnection.CheckReady(client, server, timeout.Token);
    using var response = await client.PostAsJsonAsync(new Uri(server, "api/agent/enroll"), new EnrollmentRequest(token, Environment.MachineName, rsa.ExportSubjectPublicKeyInfoPem()));
    response.EnsureSuccessStatusCode();
    var enrolled = await response.Content.ReadFromJsonAsync<EnrollmentResponse>() ?? throw new InvalidOperationException("Enrollment response missing.");
    AgentIdentity.Create(server.AbsoluteUri, enrolled.DeviceId, rsa).Save();
    Console.WriteLine($"Enrolled device {enrolled.DeviceId}. Remove the enrollment token file securely.");
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
await builder.Build().RunAsync();
