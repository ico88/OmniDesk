using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace OmniDesk.Api.Tests;
public static class DownloadScenario
{
    public static async Task Run(ApiFactory factory,HttpClient admin,HttpClient viewer)
    {
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.GetAsync("/api/admin/downloads/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.GetAsync("/api/admin/downloads/windows/kit")).StatusCode);
        var directory=Path.Combine(Path.GetTempPath(),"omnidesk-release-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var configuration=factory.Services.GetRequiredService<IConfiguration>();var old=configuration["Downloads:Directory"];configuration["Downloads:Directory"]=directory;
        try {
            Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/admin/downloads/windows")).GetProperty("available").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync("/api/admin/downloads/windows/kit")).StatusCode);
            var filename="OmniDesk.Windows.LAN-0.0.1.zip";var path=Path.Combine(directory,filename);
            using(var archive=ZipFile.Open(path,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("fixture.txt").Open());writer.Write("Isolated download fixture");}
            var bytes=await File.ReadAllBytesAsync(path);var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            async Task Manifest(string name,string digest)=>await File.WriteAllTextAsync(Path.Combine(directory,"windows-release.json"),JsonSerializer.Serialize(new {version="0.0.1",commit=new string('a',40),sha256=digest,size=bytes.Length,fileName=name,remoteEngineIncluded=false}));
            await Manifest(filename,hash);
            Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/admin/downloads/windows")).GetProperty("available").GetBoolean());
            var download=await admin.GetAsync("/api/admin/downloads/windows/kit");Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(bytes,await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("attachment",download.Content.Headers.ContentDisposition!.DispositionType);
            await Manifest("../"+filename,hash);Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync("/api/admin/downloads/windows/kit")).StatusCode);
            await Manifest(filename,new string('0',64));Assert.Equal(HttpStatusCode.ServiceUnavailable,(await admin.GetAsync("/api/admin/downloads/windows/kit")).StatusCode);
        }
        finally {configuration["Downloads:Directory"]=old;Directory.Delete(directory,true);}
    }
}
