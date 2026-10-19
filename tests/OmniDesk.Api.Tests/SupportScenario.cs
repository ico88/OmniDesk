using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OmniDesk.Contracts;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class SupportScenario
{
    public static byte[] Screenshot()
    {
        using var png = new MemoryStream(); png.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string type, byte[] bytes)
        {
            var size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size,bytes.Length); png.Write(size);
            var payload = System.Text.Encoding.ASCII.GetBytes(type).Concat(bytes).ToArray(); png.Write(payload);
            uint crc = 0xffffffff;
            foreach (var b in payload) { crc ^= b; for (var i=0;i<8;i++) crc=(crc>>1)^((crc&1)==0?0u:0xedb88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(size,~crc); png.Write(size);
        }
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header,1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),1); header[8]=8;header[9]=6;
        Chunk("IHDR",header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed,CompressionLevel.Fastest,true)) z.Write(new byte[]{0,128,128,128,255});
        Chunk("IDAT",compressed.ToArray()); Chunk("IEND",[]); return png.ToArray();
    }
    public static async Task Run(HttpClient admin, HttpClient scoped, HttpClient agent, Func<string> assertion, Guid device)
    {
        void Auth() => agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",assertion());
        var owner = new string('A',64); var other = new string('B',64);
        var create = new SupportCreate(Guid.NewGuid(),"Problema fixture","Descrizione privata",Convert.ToBase64String(Screenshot()),true);
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { ScreenshotConsent = false }))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { ScreenshotBase64 = Convert.ToBase64String(new byte[80]) }))).StatusCode);
        var corrupt = Screenshot(); corrupt[^1] ^= 1;
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { ScreenshotBase64 = Convert.ToBase64String(corrupt) }))).StatusCode);
        Auth(); var response = await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create));
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var ticket = (await response.Content.ReadFromJsonAsync<SupportTicketView>())!;
        Assert.Equal(device,ticket.DeviceId); Assert.True(ticket.HasScreenshot);
        Auth(); Assert.Equal(HttpStatusCode.OK,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.Conflict,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { Subject = "different" }))).StatusCode);
        Auth(); Assert.Empty((await agent.GetFromJsonAsync<SupportTicketView[]>($"/api/agent/support/{other}"))!);
        Auth(); Assert.Single((await agent.GetFromJsonAsync<SupportTicketView[]>($"/api/agent/support/{owner}"))!);
        Assert.DoesNotContain((await scoped.GetFromJsonAsync<SupportTicketView[]>("/api/support"))!,t=>t.Id==ticket.Id);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.GetAsync($"/api/support/{ticket.Id}/screenshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved","Done"))).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support"))!,t=>t.Id==ticket.Id);
        Assert.Single((await admin.GetFromJsonAsync<SupportTicketView[]>($"/api/support?deviceId={device}"))!);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.GetAsync($"/api/support?deviceId={device}")).StatusCode);
        var names=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/support")).EnumerateArray();
        Assert.Equal("Windows fixture",names.Single(t=>t.GetProperty("id").GetGuid()==ticket.Id).GetProperty("deviceName").GetString());
        Assert.Single((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=WINDOWS%20FIXTURE&state=open"))!);
        Assert.Empty((await scoped.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=WINDOWS%20FIXTURE"))!);
        Assert.Empty((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=%25"))!);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/support?state=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/support?search="+new string('a',201))).StatusCode);
        var image = await admin.GetAsync($"/api/support/{ticket.Id}/screenshot");
        Assert.Equal(HttpStatusCode.OK,image.StatusCode); Assert.Equal("attachment",image.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(Screenshot(),await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.GetAsync($"/api/support/{ticket.Id}/messages")).StatusCode);
        var inline = await admin.GetAsync($"/api/support/{ticket.Id}/screenshot?inline=true");
        Assert.Equal(HttpStatusCode.OK,inline.StatusCode);
        Assert.Null(inline.Content.Headers.ContentDisposition);
        Assert.True(inline.Headers.CacheControl!.NoStore);
        Assert.Equal(Screenshot(),await inline.Content.ReadAsByteArrayAsync());
        var invalid = await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved","   "));
        Assert.Contains("Scrivi una risposta",await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved",null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved","Risolto fixture"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"closed","Stale"))).StatusCode);
        var conversation = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages");
        var messages = conversation.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2,messages.Length);
        Assert.Contains(messages,m=>m.GetProperty("body").GetString()=="Risolto fixture");
        Assert.Contains(messages,m=>m.GetProperty("toState").GetString()=="resolved");
        Assert.True(conversation.GetProperty("canReply").GetBoolean());
        Assert.Equal(2,conversation.GetProperty("version").GetInt64());
        var older = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages?before={messages[1].GetProperty("id").GetInt64()}");
        Assert.Single(older.GetProperty("messages").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync($"/api/support/{ticket.Id}/messages?before=0")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=Windows%20fixture&state=open"))!);
        Assert.Single((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=Windows%20fixture&state=resolved"))!);
        Auth(); var updated = (await agent.GetFromJsonAsync<SupportTicketView[]>($"/api/agent/support/{owner}"))!.Single();
        Assert.Equal("resolved",updated.State); Assert.Equal("Risolto fixture",updated.Resolution);
        for (var i=0;i<9;i++)
        {
            Auth(); Assert.Equal(HttpStatusCode.Created,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { RequestId=Guid.NewGuid(), ScreenshotBase64=null, ScreenshotConsent=false }))).StatusCode);
        }
        Auth(); Assert.Equal(HttpStatusCode.TooManyRequests,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { RequestId=Guid.NewGuid() }))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.OK,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await admin.GetAsync($"/api/agent/support/{owner}")).StatusCode);
    }
}
