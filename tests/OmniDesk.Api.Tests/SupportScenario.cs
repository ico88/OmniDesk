using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
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
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient scoped, HttpClient agent, string scopedId, Func<string> assertion, Guid device, Guid group)
    {
        void Auth() => agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",assertion());
        var owner = new string('A',64); var other = new string('B',64);
        var queueResponse = await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = "Rete" });
        Assert.Equal(HttpStatusCode.Created, queueResponse.StatusCode);
        var queue = await queueResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var queueId = queue.GetProperty("id").GetGuid();
        var defaultQueueResponse = await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = "Smistamento generale" });
        Assert.Equal(HttpStatusCode.Created, defaultQueueResponse.StatusCode);
        var defaultQueue = await defaultQueueResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var defaultQueueId = defaultQueue.GetProperty("id").GetGuid();
        Guid siteId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            siteId = await db.Devices.Where(x => x.Id == device).Select(x => x.SiteId).SingleAsync();
        }
        var siteQueueResponse = await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = "Rete", siteId });
        Assert.Equal(HttpStatusCode.Created, siteQueueResponse.StatusCode);
        var siteQueue = await siteQueueResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var siteQueueId = siteQueue.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = " rete ", siteId })).StatusCode);
        var otherSiteResponse = await admin.PostAsJsonAsync("/api/admin/sites", new { name = "Altra sede di test" });
        Assert.Equal(HttpStatusCode.Created, otherSiteResponse.StatusCode);
        var otherSiteId = (await otherSiteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        var otherSiteQueueResponse = await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = "Rete", siteId = otherSiteId });
        Assert.Equal(HttpStatusCode.Created, otherSiteQueueResponse.StatusCode);
        var otherSiteQueueId = (await otherSiteQueueResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/support-routing", new { siteId = (Guid?)null, category = "software", queueId = defaultQueueId })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/support-routing", new { siteId, category = "software", queueId = siteQueueId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/support-routing", new { siteId, category = "software", queueId = defaultQueueId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/support-routing", new { siteId, category = "hardware", queueId = otherSiteQueueId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync("/api/admin/support-routing")).StatusCode);
        var create = new SupportCreate(Guid.NewGuid(),"Problema fixture","Descrizione privata",Convert.ToBase64String(Screenshot()),true,"software");
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { ScreenshotConsent = false }))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { ScreenshotBase64 = Convert.ToBase64String(new byte[80]) }))).StatusCode);
        var corrupt = Screenshot(); corrupt[^1] ^= 1;
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { ScreenshotBase64 = Convert.ToBase64String(corrupt) }))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.BadRequest,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { RequestId=Guid.NewGuid(), Category="unlisted" }))).StatusCode);
        Auth(); var response = await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create));
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var ticket = (await response.Content.ReadFromJsonAsync<SupportTicketView>())!;
        Assert.True(ticket.Number > 0);
        Assert.Equal("software", ticket.Category);
        Assert.Equal(device,ticket.DeviceId); Assert.True(ticket.HasScreenshot);
        Assert.Equal(siteQueueId, (await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!.Single().GetProperty("queueId").GetGuid());
        var availableQueues = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/queues");
        Assert.Contains(availableQueues!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        Assert.Contains(availableQueues!, x => x.GetProperty("id").GetGuid() == queueId);
        Assert.DoesNotContain(availableQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        var routingRules = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/admin/support-routing");
        var siteRule = routingRules!.Single(x => x.GetProperty("siteId").ValueKind == System.Text.Json.JsonValueKind.String && x.GetProperty("siteId").GetGuid() == siteId);
        var siteRuleId = siteRule.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/support-routing/{siteRuleId}", new { expectedVersion = 1, siteId, category = "software", queueId = siteQueueId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/support-routing/{siteRuleId}", new { expectedVersion = 1, siteId, category = "software", queueId = siteQueueId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/support-routing/{siteRuleId}") { Content = JsonContent.Create(new { expectedVersion = 1 }) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/support-routing/{siteRuleId}") { Content = JsonContent.Create(new { expectedVersion = 2 }) })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = await db.CoreOutboxEvents.SingleAsync(x => x.EventType == "ticket.created" && x.AggregateId == ticket.Id);
            Assert.Contains(device.ToString(), evt.Payload);
            Assert.DoesNotContain(create.Description, evt.Payload);
            Assert.DoesNotContain(create.ScreenshotBase64!, evt.Payload);
        }
        Auth(); Assert.Equal(HttpStatusCode.OK,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.Conflict,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { Subject = "different" }))).StatusCode);
        Auth(); Assert.Empty((await agent.GetFromJsonAsync<SupportTicketView[]>($"/api/agent/support/{other}"))!);
        Auth(); Assert.Single((await agent.GetFromJsonAsync<SupportTicketView[]>($"/api/agent/support/{owner}"))!);
        Assert.DoesNotContain((await scoped.GetFromJsonAsync<SupportTicketView[]>("/api/support"))!,t=>t.Id==ticket.Id);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.GetAsync($"/api/support/{ticket.Id}/screenshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved","Done"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.PutAsJsonAsync("/api/support/bulk-queue",new { queueId=(Guid?)null,tickets=new[]{new{ticketId=ticket.Id,expectedVersion=1L}} })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.GetAsync($"/api/support/{ticket.Id}/assignees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await scoped.GetAsync($"/api/support/{ticket.Id}/queues")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync($"/api/support/queues?siteId={siteId}")).StatusCode);
        var ticketAssignees=await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.Empty(ticketAssignees!);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support/queues"))!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        var adminOtherSiteQueues = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/queues?siteId={otherSiteId}");
        Assert.Contains(adminOtherSiteQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/support/queues?siteId={Guid.NewGuid()}")).StatusCode);
        var adminSiteQueues = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/queues?siteId={siteId}");
        Assert.Contains(adminSiteQueues!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        Assert.DoesNotContain(adminSiteQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        Assert.Equal("normal",(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!.Single().GetProperty("priority").GetString());
        Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync("/api/admin/roles",new {name="Ticket technician",permissions=new[]{"tickets.view","tickets.manage"}})).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync("/api/admin/grants",new {userId=scopedId,role="Ticket technician",groupId=group,deviceId=(Guid?)null})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = " rete " })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync("/api/admin/support-queues", new { name = "No access" })).StatusCode);
        Assert.Contains((await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support/queues"))!, x => x.GetProperty("id").GetGuid() == queueId);
        var scopedSiteQueues = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/queues?siteId={siteId}");
        Assert.Contains(scopedSiteQueues!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        Assert.DoesNotContain(scopedSiteQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        var scopedTicketQueues = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/queues");
        Assert.Contains(scopedTicketQueues!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        Assert.DoesNotContain(scopedTicketQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/support/bulk-queue", new { queueId = otherSiteQueueId, tickets = new[] { new { ticketId = ticket.Id, expectedVersion = 1L } } })).StatusCode);
        var scopedAssignees=await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.DoesNotContain(scopedAssignees!,x=>x.GetProperty("id").GetString()==scopedId);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}/members",new {expectedVersion=1,userIds=new[]{"unknown-user"}})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}/members",new {expectedVersion=1,userIds=new[]{scopedId}})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}/members",new {expectedVersion=1,userIds=Array.Empty<string>()})).StatusCode);
        scopedAssignees=await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.Contains(scopedAssignees!,x=>x.GetProperty("id").GetString()==scopedId);
        var adminUser=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/admin/users"))!.Single(x=>x.GetProperty("email").GetString()=="admin@example.test").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=1,state="open",resolution=(string?)null,queueId=otherSiteQueueId,updateQueue=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=1,state="open",resolution=(string?)null,queueId=siteQueueId,updateQueue=true,assigneeId=adminUser,updateAssignment=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=1,state="open",resolution=(string?)null,priority="emergency",updatePriority=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=1,state="open",resolution=(string?)null,assigneeId="new-user-without-scope",updateAssignment=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=1,state="open",resolution=(string?)null,queueId=Guid.NewGuid(),updateQueue=true})).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support"))!,t=>t.Id==ticket.Id);
        var scopedSummary=await scoped.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/summary?deviceId={device}");
        Assert.Equal(1,scopedSummary.GetProperty("total").GetInt32());
        Assert.Equal(1,scopedSummary.GetProperty("open").GetInt32());
        Assert.Equal(0,scopedSummary.GetProperty("urgent").GetInt32());
        Assert.Single((await admin.GetFromJsonAsync<SupportTicketView[]>($"/api/support?deviceId={device}"))!);
        Assert.Contains((await scoped.GetFromJsonAsync<SupportTicketView[]>($"/api/support?deviceId={device}"))!,t=>t.Id==ticket.Id);
        var names=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/support")).EnumerateArray();
        Assert.Equal("Windows fixture",names.Single(t=>t.GetProperty("id").GetGuid()==ticket.Id).GetProperty("deviceName").GetString());
        Assert.Single((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=WINDOWS%20FIXTURE&state=open"))!);
        Assert.Single((await scoped.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=WINDOWS%20FIXTURE"))!);
        Assert.Empty((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=%25"))!);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/support?state=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/support?priority=emergency")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/support?category=unlisted")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/support?search="+new string('a',201))).StatusCode);
        var image = await admin.GetAsync($"/api/support/{ticket.Id}/screenshot");
        Assert.Equal(HttpStatusCode.OK,image.StatusCode); Assert.Equal("attachment",image.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(Screenshot(),await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK,(await scoped.GetAsync($"/api/support/{ticket.Id}/messages")).StatusCode);
        var inline = await admin.GetAsync($"/api/support/{ticket.Id}/screenshot?inline=true");
        Assert.Equal(HttpStatusCode.OK,inline.StatusCode);
        Assert.Null(inline.Content.Headers.ContentDisposition);
        Assert.True(inline.Headers.CacheControl!.NoStore);
        Assert.Equal(Screenshot(),await inline.Content.ReadAsByteArrayAsync());
        var invalid = await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved","   "));
        Assert.Contains("Scrivi una risposta",await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"resolved",null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=1,state="resolved",resolution="Risolto fixture",addReply=true,priority="urgent",updatePriority=true,queueId=siteQueueId,updateQueue=true,assigneeId=scopedId,updateAssignment=true})).StatusCode);
        var assignedTicket=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!.Single();
        Assert.Equal("urgent",assignedTicket.GetProperty("priority").GetString());
        Assert.Equal("software",assignedTicket.GetProperty("category").GetString());
        Assert.Equal(siteQueueId,assignedTicket.GetProperty("queueId").GetGuid());
        Assert.Equal("Rete",assignedTicket.GetProperty("queueName").GetString());
        var queueAssignees=await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.Contains(queueAssignees!,x=>x.GetProperty("id").GetString()==scopedId);
        Assert.DoesNotContain(queueAssignees!,x=>x.GetProperty("id").GetString()==adminUser);
        Assert.True(assignedTicket.GetProperty("number").GetInt64() > 0);
        Assert.Equal(scopedId,assignedTicket.GetProperty("assigneeId").GetString());
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?priority=urgent"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support?category=software"))!);
        Assert.Empty((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support?category=hardware"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?queue={siteQueueId}"))!);
        Assert.Empty((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support?queue=unassigned"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?assignee={Uri.EscapeDataString("scoped@example.test")}"))!);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = await db.CoreOutboxEvents.SingleAsync(x => x.EventType == "ticket.updated" && x.AggregateId == ticket.Id);
            Assert.Contains("resolved", evt.Payload);
            Assert.DoesNotContain("Risolto fixture", evt.Payload);
        }
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"closed","Stale"))).StatusCode);
        var conversation = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages");
        var messages = conversation.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2,messages.Length);
        Assert.Contains(messages,m=>m.GetProperty("body").GetString()=="Risolto fixture");
        Assert.Contains(messages,m=>m.GetProperty("toState").GetString()=="resolved");
        Assert.True(conversation.GetProperty("canReply").GetBoolean());
        Assert.Equal(2,conversation.GetProperty("version").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/support/bulk-queue", new { queueId = (Guid?)null, tickets = new[] { new { ticketId = ticket.Id, expectedVersion = 2L } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/support/bulk-queue", new { queueId = (Guid?)null, tickets = new[] { new { ticketId = ticket.Id, expectedVersion = 2L } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}", new { name = "Rete", expectedName = "Rete", expectedVersion = 2, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/support/{ticket.Id}", new { expectedVersion = 3, state = "resolved", resolution = "Risolto fixture", queueId = siteQueueId, updateQueue = true })).StatusCode);
        var older = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages?before={messages[1].GetProperty("id").GetInt64()}");
        Assert.Single(older.GetProperty("messages").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync($"/api/support/{ticket.Id}/messages?before=0")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=Windows%20fixture&state=open"))!);
        Assert.Single((await admin.GetFromJsonAsync<SupportTicketView[]>("/api/support?search=Windows%20fixture&state=resolved"))!);
        Auth(); var updated = (await agent.GetFromJsonAsync<SupportTicketView[]>($"/api/agent/support/{owner}"))!.Single();
        Assert.Equal("resolved",updated.State); Assert.Equal("Risolto fixture",updated.Resolution);
        for (var i=0;i<9;i++)
        {
            Auth(); var routed = await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { RequestId=Guid.NewGuid(), ScreenshotBase64=null, ScreenshotConsent=false }));
            Assert.Equal(HttpStatusCode.Created, routed.StatusCode);
            if (i == 0)
            {
                var newTicket = (await routed.Content.ReadFromJsonAsync<SupportTicketView>())!;
                Assert.Equal(defaultQueueId, (await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!.Single(x => x.GetProperty("id").GetGuid() == newTicket.Id).GetProperty("queueId").GetGuid());
            }
        }
        Auth(); Assert.Equal(HttpStatusCode.TooManyRequests,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create with { RequestId=Guid.NewGuid() }))).StatusCode);
        Auth(); Assert.Equal(HttpStatusCode.OK,(await agent.PostAsJsonAsync("/api/agent/support",new SupportIntake(owner,create))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await admin.GetAsync($"/api/agent/support/{owner}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/support/{ticket.Id}/internal-notes", new { expectedVersion = 3L, body = "nota riservata" })).StatusCode);
        var beforeNote = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages");
        var noteVersion = beforeNote.GetProperty("version").GetInt64();
        Assert.True(beforeNote.GetProperty("canNote").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/support/{ticket.Id}/internal-notes", new { expectedVersion = noteVersion, body = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support/{ticket.Id}/internal-notes", new { expectedVersion = noteVersion, body = "Nota interna solo per assistenza" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/support/{ticket.Id}/internal-notes", new { expectedVersion = noteVersion, body = "versione obsoleta" })).StatusCode);
        var staffThread = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages");
        Assert.Contains(staffThread.GetProperty("messages").EnumerateArray(), m => m.GetProperty("visibility").GetString() == "internal" && m.GetProperty("body").GetString() == "Nota interna solo per assistenza");
        var scopedThread = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages");
        Assert.False(scopedThread.GetProperty("canNote").GetBoolean());
        Assert.DoesNotContain(scopedThread.GetProperty("messages").EnumerateArray(), m => m.GetProperty("body").GetString() == "Nota interna solo per assistenza");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = await db.AuditEvents.SingleAsync(x => x.Action == "ticket.note.internal.created" && x.Target == ticket.Id.ToString());
            Assert.Equal(adminUser, audit.ActorId);
            var evt = await db.CoreOutboxEvents.SingleAsync(x => x.EventType == "ticket.internal_note.created" && x.AggregateId == ticket.Id);
            Assert.DoesNotContain("Nota interna solo per assistenza", evt.Payload);
        }
    }
}
