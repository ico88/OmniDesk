using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
namespace OmniDesk.Api.Endpoints;

public sealed record WindowsRelease(string Version,string Commit,string Sha256,long Size,string FileName,bool RemoteEngineIncluded,string? MsiSha256=null,long? MsiSize=null);
public sealed record RustDeskRelease(string Version,string Sha256,long Size,string FileName);
public static class DownloadEndpoints
{
    public static RustDeskRelease? ReadRustDeskRelease(IConfiguration config)
    {
        var directory=config["RustDesk:InstallerDirectory"]??config["Downloads:Directory"];
        if(string.IsNullOrWhiteSpace(directory))return null;
        var manifest=Path.Combine(directory,"rustdesk-release.json");
        if(!File.Exists(manifest)||new FileInfo(manifest).Length>4096)return null;
        try {
            var release=JsonSerializer.Deserialize<RustDeskRelease>(File.ReadAllText(manifest),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(release is null||!Regex.IsMatch(release.Version??@"",@"\A[0-9]+\.[0-9]+\.[0-9]+\z")||
                !Regex.IsMatch(release.Sha256??@"",@"\A[a-f0-9]{64}\z")||release.Size is < 1 or > 104857600||
                release.FileName!=$"rustdesk-{release.Version}-x86_64.exe")return null;
            var file=new FileInfo(Path.Combine(directory,release.FileName));
            return file.Exists&&file.Length==release.Size?release:null;
        }
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    public static WindowsRelease? ReadRelease(IConfiguration config)
    {
        var directory=config["Downloads:Directory"];
        if(string.IsNullOrWhiteSpace(directory))return null;
        var manifest=Path.Combine(directory,"windows-release.json");
        if(!File.Exists(manifest)||new FileInfo(manifest).Length>4096)return null;
        try {
            var release=JsonSerializer.Deserialize<WindowsRelease>(File.ReadAllText(manifest),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(release is null||!Regex.IsMatch(release.Version??"",@"\A[0-9]+\.[0-9]+\.[0-9]+\z")||
                !Regex.IsMatch(release.Commit??"",@"\A[a-f0-9]{40}\z")||!Regex.IsMatch(release.Sha256??"",@"\A[a-f0-9]{64}\z")||
                release.Size is < 1 or > 268435456||release.FileName!=$"OmniDesk.Windows.LAN-{release.Version}.zip"||
                ((release.MsiSha256 is null) != (release.MsiSize is null)) ||
                (release.MsiSha256 is not null && (!Regex.IsMatch(release.MsiSha256, @"\A[a-f0-9]{64}\z") || release.MsiSize is < 1 or > 268435456)))return null;
            var file=new FileInfo(Path.Combine(directory,release.FileName));
            return file.Exists&&file.Length==release.Size?release:null;
        }
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    public static void MapDownloadEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/api/admin/downloads").RequireAuthorization().AddEndpointFilter(async(c,next)=>
            await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(c.HttpContext.User.UserId(),"administration")?await next(c):Results.Forbid());
        group.MapGet("/windows",(IConfiguration config)=>{
            var release=ReadRelease(config);
            return Results.Ok(new {available=release!=null,release,url=release is null?null:"/api/admin/downloads/windows/kit"});
        });
        group.MapGet("/windows/kit",async(HttpContext c,IConfiguration config,AppDbContext db)=>{
            var release=ReadRelease(config);if(release is null)return Results.NotFound();
            var stream=new FileStream(Path.Combine(config["Downloads:Directory"]!,release.FileName),FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            try {
                var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,c.RequestAborted)).ToLowerInvariant();
                if(hash!=release.Sha256){await stream.DisposeAsync();return Results.Problem("Package integrity check failed",statusCode:503);}
                stream.Position=0;
                db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action="package.download",Target="windows:"+release.Version});
                await db.SaveChangesAsync(c.RequestAborted);
                return Results.File(stream,"application/zip",release.FileName);
            }
            catch {await stream.DisposeAsync();throw;}
        });
        group.MapGet("/rustdesk",(IConfiguration config)=>{
            var release=ReadRustDeskRelease(config);
            var server=config["RustDesk:IdServer"]??"";
            var relay=config["RustDesk:RelayServer"]??"";
            var key=config["RustDesk:PublicKey"]??"";
            return Results.Ok(new {available=release is not null,release,url=release is null?null:"/api/admin/downloads/rustdesk/installer",idServer=server,relayServer=relay,apiServer="",publicKey=key});
        });
        group.MapGet("/rustdesk/installer",async(HttpContext c,IConfiguration config,AppDbContext db)=>{
            var release=ReadRustDeskRelease(config);if(release is null)return Results.NotFound();
            var stream=new FileStream(Path.Combine(config["RustDesk:InstallerDirectory"]??config["Downloads:Directory"]!,release.FileName),FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            try {
                var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,c.RequestAborted)).ToLowerInvariant();
                if(hash!=release.Sha256){await stream.DisposeAsync();return Results.Problem("Package integrity check failed",statusCode:503);}
                stream.Position=0;
                db.AuditEvents.Add(new AuditEvent {ActorId=c.User.UserId(),Action="package.download",Target="rustdesk:"+release.Version});
                await db.SaveChangesAsync(c.RequestAborted);
                return Results.File(stream,"application/vnd.microsoft.portable-executable",release.FileName);
            }
            catch {await stream.DisposeAsync();throw;}
        });
    }
}
