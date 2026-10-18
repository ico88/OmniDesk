using System.Net.Http.Json;
using System.Security.Cryptography;
using OmniDesk.Agent.Windows;
using OmniDesk.Contracts;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("OmniDesk Agent requires Windows.");
if (args.Length == 1 && args[0] == "self-test") { await WindowsSelfTest.Run(); return; }
if (args.Length > 0 && args[0] == "enroll")
{
    string Option(string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {key}"); }
    if (AgentIdentity.Load() is not null) throw new InvalidOperationException("Already enrolled. Revoke the old identity and remove its local configuration before enrolling again.");
    var server = AgentIdentity.ValidateServer(Option("--server"));
    var tokenPath = Option("--token-file");
    var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
    if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid enrollment token file.");
    // Establish ACLs before contacting server, so an unprivileged enrollment cannot consume a token.
    var directory = Directory.CreateDirectory(AgentIdentity.Folder);
    var acl = new System.Security.AccessControl.DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
    acl.SetOwner(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null));
    foreach (var sid in new[] { System.Security.Principal.WellKnownSidType.LocalSystemSid, System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid })
        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(sid, null),
            System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
    System.IO.FileSystemAclExtensions.SetAccessControl(directory, acl);
    using var rsa = RSA.Create(3072);
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    using var response = await client.PostAsJsonAsync(new Uri(server, "api/agent/enroll"), new EnrollmentRequest(token, Environment.MachineName, rsa.ExportSubjectPublicKeyInfoPem()));
    response.EnsureSuccessStatusCode();
    var enrolled = await response.Content.ReadFromJsonAsync<EnrollmentResponse>() ?? throw new InvalidOperationException("Enrollment response missing.");
    AgentIdentity.Create(server.AbsoluteUri, enrolled.DeviceId, rsa).Save();
    Console.WriteLine($"Enrolled device {enrolled.DeviceId}. Remove the enrollment token file securely.");
    return;
}
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "OmniDeskAgent");
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<UpdateWorker>();
await builder.Build().RunAsync();
