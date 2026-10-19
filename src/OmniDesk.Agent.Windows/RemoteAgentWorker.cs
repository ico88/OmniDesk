using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using OmniDesk.Contracts;
namespace OmniDesk.Agent.Windows;

public sealed class RemoteAgentWorker(ILogger<RemoteAgentWorker> logger) : BackgroundService
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"OmniDesk Agent","Remote");
    public static string ExecutablePath => Path.Combine(DirectoryPath,RemoteAgentPackage.ExecutableName);
    private Guid? bound;
    private Guid? configured;
    private Stopwatch? authorized;
    private TimeSpan lease;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client=AgentConnection.CreateClient();
        try {
            while(!stoppingToken.IsCancellationRequested) {
                try {
                    var identity=AgentIdentity.Load();
                    if(identity is null) { authorized=null;StopRemote(); }
                    else {
                        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        using var request=Message(identity,HttpMethod.Get,"configuration");
                        using var response=await client.SendAsync(request,timeout.Token);
                        if(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {authorized=null;StopRemote();}
                        else {
                            response.EnsureSuccessStatusCode();
                            var config=await response.Content.ReadFromJsonAsync<RemoteAgentConfiguration>(timeout.Token)??throw new InvalidDataException();
                            var remaining=config.AuthorizedUntil-DateTimeOffset.UtcNow;
                            if(remaining<=TimeSpan.Zero)throw new InvalidDataException("Expired remote configuration");
                            ValidateSettings(config.Settings);
                            if(configured!=config.Id) {
                                StopRemote();await ValidateExecutable(timeout.Token);
                                AgentIdentity.WriteTrusted(Path.Combine(DirectoryPath,"MeshService64.msh"),config.Settings);
                                configured=config.Id;bound=null;
                            }
                            lease=remaining<TimeSpan.FromSeconds(60)?remaining:TimeSpan.FromSeconds(60);authorized=Stopwatch.StartNew();
                            using var service=new ServiceController(RemoteAgentPackage.ServiceName);service.Refresh();
                            if(service.Status!=ServiceControllerStatus.Running){service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(10));bound=null;}
                            if(bound!=config.Id) {
                                var node=await ReadNode(timeout.Token);
                                using var report=Message(identity,HttpMethod.Post,"node");report.Content=JsonContent.Create(new RemoteNodeReport(config.Id,node));
                                using var result=await client.SendAsync(report,timeout.Token);result.EnsureSuccessStatusCode();bound=config.Id;
                            }
                        }
                    }
                }
                catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception e){logger.LogWarning("Remote provisioning/lease unavailable: {Type}",e.GetType().Name);}
                if(authorized is null||authorized.Elapsed>=lease)StopRemote();
                await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);
            }
        }
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
        finally {StopRemote();}
    }
    private static HttpRequestMessage Message(AgentIdentity identity,HttpMethod method,string path)
    {
        var request=new HttpRequestMessage(method,new Uri(AgentIdentity.ValidateServer(identity.ServerUrl),"api/agent/remote/"+path));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",identity.Assertion());return request;
    }
    private void StopRemote()
    {
        try {
            using var service=new ServiceController(RemoteAgentPackage.ServiceName);service.Refresh();
            if(service.Status!=ServiceControllerStatus.Stopped){service.Stop();service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(10));}
            bound=null;
        }
        catch(InvalidOperationException e) when(e.InnerException is System.ComponentModel.Win32Exception {NativeErrorCode:1060}){ /* Agent-only packages do not install the vendor service. */ }
        catch(Exception e){logger.LogError("Remote service shutdown failed: {Type}",e.GetType().Name);}
    }
    private static async Task ValidateExecutable(CancellationToken ct)
    {
        AgentIdentity.AssertTrusted(new DirectoryInfo(DirectoryPath));
        var file=new FileInfo(ExecutablePath);
        if((file.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException();
        var acl=file.GetAccessControl();
        var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);var admin=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
        if(!system.Equals(acl.GetOwner(typeof(SecurityIdentifier)))&&!admin.Equals(acl.GetOwner(typeof(SecurityIdentifier))))throw new UnauthorizedAccessException();
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
            if(rule.AccessControlType==AccessControlType.Allow&&!system.Equals(rule.IdentityReference)&&!admin.Equals(rule.IdentityReference)&&
                (rule.FileSystemRights&(FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership))!=0)throw new UnauthorizedAccessException();
        await using var stream=File.OpenRead(ExecutablePath);
        if(!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)),RemoteAgentPackage.Sha256,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Remote binary hash mismatch");
    }
    public static void ValidateSettings(string settings)
    {
        if(settings.Length>4096)throw new InvalidDataException();
        var rows=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var line in settings.Split('\n',StringSplitOptions.RemoveEmptyEntries)){
            var parts=line.TrimEnd('\r').Split('=',2);if(parts.Length!=2||!rows.TryAdd(parts[0],parts[1]))throw new InvalidDataException();
        }
        if(rows.Count!=8||!rows.TryGetValue("MeshServer",out var value)||!Uri.TryCreate(value,UriKind.Absolute,out var server)||
            server.Scheme!="wss"||server.AbsolutePath!="/agent.ashx"||server.UserInfo!=""||server.Query!=""||server.Fragment!=""||
            rows.GetValueOrDefault("meshServiceName")!=RemoteAgentPackage.ServiceName||rows.GetValueOrDefault("disableUpdate")!="1"||rows.GetValueOrDefault("MeshType")!="2"||
            !Regex.IsMatch(rows.GetValueOrDefault("ServerID")??"",@"\A[0-9A-F]{96}\z")||!Regex.IsMatch(rows.GetValueOrDefault("MeshID")??"",@"\A0x[0-9A-F]{96}\z")||
            !Guid.TryParseExact(rows.GetValueOrDefault("Tag"),"N",out var id)||rows.GetValueOrDefault("MeshName")!="OmniDesk:"+id.ToString("N"))throw new InvalidDataException();
    }
    private static async Task<string> ReadNode(CancellationToken ct)
    {
        var start=new ProcessStartInfo(ExecutablePath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=DirectoryPath};
        start.ArgumentList.Add("-nodeid");using var process=Process.Start(start)??throw new IOException();
        try {
            var output=process.StandardOutput.ReadToEndAsync(ct);var error=process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);var text=(await output).Trim();_ = await error;
            if(process.ExitCode!=0||text.Length>128)throw new IOException();
            text=text.Replace('+','@').Replace('/','$');
            if(!Regex.IsMatch(text,@"\A[A-Za-z0-9@$=_-]{64}\z"))throw new InvalidDataException("Invalid native node identity");
            return "node//"+text;
        }
        finally {if(!process.HasExited)process.Kill();}
    }
}
