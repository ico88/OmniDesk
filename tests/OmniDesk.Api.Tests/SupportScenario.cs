using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniDesk.Api.Data;
using OmniDesk.Api.Endpoints;
using OmniDesk.Api.Security;
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
        Auth();
        var requesterThread = await agent.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/agent/support/{owner}/{ticket.Id}/messages");
        Assert.Single(requesterThread.GetProperty("messages").EnumerateArray());
        Assert.Equal(create.Description, requesterThread.GetProperty("messages")[0].GetProperty("body").GetString());
        Auth();
        Assert.Equal(HttpStatusCode.NotFound, (await agent.PostAsJsonAsync($"/api/agent/support/{other}/{ticket.Id}/messages", new SupportRequesterReply(other, 1, "No access"))).StatusCode);
        Auth();
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/support/{owner}/{ticket.Id}/messages", new SupportRequesterReply(owner, 1, "Il problema è ancora presente."))).StatusCode);
        Auth();
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync($"/api/agent/support/{owner}/{ticket.Id}/messages", new SupportRequesterReply(owner, 1, "Risposta obsoleta"))).StatusCode);
        Auth();
        requesterThread = await agent.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/agent/support/{owner}/{ticket.Id}/messages");
        Assert.Equal(2, requesterThread.GetProperty("version").GetInt64());
        Assert.Contains(requesterThread.GetProperty("messages").EnumerateArray(), m => m.GetProperty("body").GetString() == "Il problema è ancora presente.");
        Guid remoteSessionId = Guid.NewGuid(), updateJobId = Guid.NewGuid();
        var utcNow = DateTimeOffset.UtcNow;
        var jobStartedAt = new DateTimeOffset(utcNow.Ticks - utcNow.Ticks % 10, TimeSpan.Zero).AddMinutes(-2);
        var jobCompletedAt = jobStartedAt.AddSeconds(45);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RemoteSessions.Add(new RemoteSession { Id = remoteSessionId, DeviceId = device, Capability = "remote.control", State = "revoked", ActorId = "fixture", EndedAt = jobCompletedAt });
            db.UpdateJobs.Add(new UpdateJob { Id = updateJobId, DeviceId = device, RequestId = Guid.NewGuid(), RequestedBy = "fixture",
                Kind = "diagnostics.processes", State = "succeeded", CreatedAt = jobStartedAt, CompletedAt = jobCompletedAt,
                Result = "{\"output\":\"sensitive diagnostic output must not be exposed\"}" });
            await db.SaveChangesAsync();
        }
        var interventionUrl = $"/api/support/{ticket.Id}/interventions";
        var candidateUrl = $"/api/support/{ticket.Id}/intervention-candidates";
        var rmmCandidates = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>(candidateUrl);
        Assert.Contains(rmmCandidates!, x => x.GetProperty("id").GetGuid() == remoteSessionId);
        Assert.Contains(rmmCandidates!, x => x.GetProperty("id").GetGuid() == updateJobId);
        Assert.Equal(jobCompletedAt, rmmCandidates!.Single(x => x.GetProperty("id").GetGuid() == updateJobId).GetProperty("completedAt").GetDateTimeOffset());
        var remoteLink = new { expectedVersion = 2L, deviceId = device, artifactType = "remote_session", artifactId = remoteSessionId };
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync(interventionUrl, remoteLink)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(interventionUrl, new { expectedVersion = 2L, deviceId = Guid.NewGuid(), artifactType = remoteLink.artifactType, artifactId = remoteSessionId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(interventionUrl, new { expectedVersion = 2L, deviceId = device, artifactType = "arbitrary", artifactId = remoteSessionId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(interventionUrl, remoteLink)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(interventionUrl, remoteLink)).StatusCode);
        var jobLink = new { expectedVersion = 3L, deviceId = device, artifactType = "update_job", artifactId = updateJobId };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(interventionUrl, jobLink)).StatusCode);
        var linkedJobSnapshot = (await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/interventions"))!
            .Single(x => x.GetProperty("artifactId").GetGuid() == updateJobId);
        Assert.Equal(jobStartedAt, linkedJobSnapshot.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(jobCompletedAt, linkedJobSnapshot.GetProperty("completedAt").GetDateTimeOffset());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UpdateJobs.Where(x => x.Id == updateJobId).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "failed"));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(interventionUrl, jobLink)).StatusCode);
        var interventions = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/interventions");
        Assert.Equal(2, interventions!.Length);
        Assert.Contains(interventions, x => x.GetProperty("artifactId").GetGuid() == remoteSessionId && x.GetProperty("capability").GetString() == "remote.control");
        Assert.Equal(jobCompletedAt, interventions.Single(x => x.GetProperty("artifactId").GetGuid() == remoteSessionId).GetProperty("completedAt").GetDateTimeOffset());
        var linkedJob = interventions.Single(x => x.GetProperty("artifactId").GetGuid() == updateJobId);
        Assert.Equal("diagnostics.view", linkedJob.GetProperty("capability").GetString());
        Assert.Equal("succeeded", linkedJob.GetProperty("state").GetString());
        Assert.Equal("failed", linkedJob.GetProperty("currentState").GetString());
        Assert.Equal(jobCompletedAt, linkedJob.GetProperty("completedAt").GetDateTimeOffset());
        Assert.DoesNotContain(System.Text.Json.JsonSerializer.Serialize(interventions), "sensitive diagnostic output");
        var remainingCandidates = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>(candidateUrl);
        Assert.DoesNotContain(remainingCandidates!, x => x.GetProperty("id").GetGuid() == remoteSessionId);
        Assert.DoesNotContain(remainingCandidates!, x => x.GetProperty("id").GetGuid() == updateJobId);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync($"/api/support/{ticket.Id}/interventions")).StatusCode);
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
        Assert.Equal("p3",(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!.Single().GetProperty("priority").GetString());
        Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync("/api/admin/roles",new {name="Ticket technician",permissions=new[]{"tickets.view","tickets.manage"}})).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync("/api/admin/grants",new {userId=scopedId,role="Ticket technician",groupId=group,deviceId=(Guid?)null})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/support-queues", new { name = " rete " })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync("/api/admin/support-queues", new { name = "No access" })).StatusCode);
        Assert.Contains((await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support/queues"))!, x => x.GetProperty("id").GetGuid() == queueId);
        var scopedSiteQueues = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/queues?siteId={siteId}");
        Assert.Contains(scopedSiteQueues!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        Assert.DoesNotContain(scopedSiteQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        var scopedInterventions = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/interventions");
        Assert.Empty(scopedInterventions!);
        Assert.Empty(await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>(candidateUrl));
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync(interventionUrl, new { expectedVersion = 4L, deviceId = device, artifactType = "update_job", artifactId = updateJobId })).StatusCode);
        var scopedTicketQueues = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/queues");
        Assert.Contains(scopedTicketQueues!, x => x.GetProperty("id").GetGuid() == siteQueueId);
        Assert.DoesNotContain(scopedTicketQueues!, x => x.GetProperty("id").GetGuid() == otherSiteQueueId);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/support/bulk-queue", new { queueId = otherSiteQueueId, tickets = new[] { new { ticketId = ticket.Id, expectedVersion = 4L } } })).StatusCode);
        var scopedAssignees=await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.DoesNotContain(scopedAssignees!,x=>x.GetProperty("id").GetString()==scopedId);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}/members",new {expectedVersion=1,userIds=new[]{"unknown-user"}})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}/members",new {expectedVersion=1,userIds=new[]{scopedId}})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}/members",new {expectedVersion=1,userIds=Array.Empty<string>()})).StatusCode);
        scopedAssignees=await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.Contains(scopedAssignees!,x=>x.GetProperty("id").GetString()==scopedId);
        var notificationWorker = new TicketNotificationWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TicketNotificationWorker>.Instance);
        while (await notificationWorker.ProcessNext(CancellationToken.None)) { }
        var inbox = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/notifications");
        var ticketNotices = inbox.GetProperty("notifications").EnumerateArray().Where(x => x.GetProperty("ticketId").GetGuid() == ticket.Id).ToArray();
        var newTicketNotice = ticketNotices.Single(x => x.GetProperty("kind").GetString() == "ticket.new");
        var replyNotice = ticketNotices.Single(x => x.GetProperty("kind").GetString() == "ticket.reply");
        Assert.Equal(2, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/notifications/{newTicketNotice.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.PostAsync($"/api/notifications/{newTicketNotice.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.PostAsync($"/api/notifications/{replyNotice.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        inbox = await scoped.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/notifications");
        Assert.Equal(0, inbox.GetProperty("unreadCount").GetInt32());
        Assert.False(await notificationWorker.ProcessNext(CancellationToken.None));
        var brokenOutboxId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CoreOutboxEvents.Add(new CoreOutboxEvent { Id=brokenOutboxId, EventType="ticket.created", AggregateType="ticket", AggregateId=ticket.Id, Payload="[]" });
            await db.SaveChangesAsync();
        }
        for (var attempt=1;attempt<=10;attempt++)
        {
            Assert.True(await notificationWorker.ProcessNext(CancellationToken.None));
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.CoreOutboxDeliveries.SingleAsync(x=>x.EventId==brokenOutboxId);
            Assert.Equal(attempt,delivery.AttemptCount);
            if(attempt<10)
            {
                Assert.Null(delivery.DeadLetteredAt);
                delivery.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            else
            {
                Assert.NotNull(delivery.DeadLetteredAt);
                Assert.Equal("JsonException",delivery.LastErrorType);
            }
        }
        Assert.False(await notificationWorker.ProcessNext(CancellationToken.None));
        var adminUser=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/admin/users"))!.Single(x=>x.GetProperty("email").GetString()=="admin@example.test").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=4,state="open",resolution=(string?)null,queueId=otherSiteQueueId,updateQueue=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=4,state="open",resolution=(string?)null,queueId=siteQueueId,updateQueue=true,assigneeId=adminUser,updateAssignment=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=4,state="open",resolution=(string?)null,priority="emergency",updatePriority=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=4,state="open",resolution=(string?)null,assigneeId="new-user-without-scope",updateAssignment=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=4,state="open",resolution=(string?)null,queueId=Guid.NewGuid(),updateQueue=true})).StatusCode);
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
        var invalid = await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(4,"resolved","   "));
        Assert.Contains("Scrivi una risposta",await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(4,"resolved",null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(4,"closed","Risolto"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new {expectedVersion=4,state="resolved",resolution="Risolto fixture",addReply=true,priority="urgent",updatePriority=true,queueId=siteQueueId,updateQueue=true,assigneeId=scopedId,updateAssignment=true})).StatusCode);
        var assignedTicket=(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!.Single();
        Assert.Equal("p1",assignedTicket.GetProperty("priority").GetString());
        Assert.Equal("software",assignedTicket.GetProperty("category").GetString());
        Assert.Equal(siteQueueId,assignedTicket.GetProperty("queueId").GetGuid());
        Assert.Equal("Rete",assignedTicket.GetProperty("queueName").GetString());
        var queueAssignees=await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{ticket.Id}/assignees");
        Assert.Contains(queueAssignees!,x=>x.GetProperty("id").GetString()==scopedId);
        Assert.DoesNotContain(queueAssignees!,x=>x.GetProperty("id").GetString()==adminUser);
        Assert.True(assignedTicket.GetProperty("number").GetInt64() > 0);
        Assert.Equal(scopedId,assignedTicket.GetProperty("assigneeId").GetString());
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?priority=p1"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?priority=urgent"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support?category=software"))!);
        Assert.Empty((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support?category=hardware"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?queue={siteQueueId}"))!);
        Assert.Empty((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/support?queue=unassigned"))!);
        Assert.Single((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?assignee={Uri.EscapeDataString("scoped@example.test")}"))!);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var events = await db.CoreOutboxEvents.Where(x => (x.EventType == "ticket.updated" || x.EventType == "ticket.requester_replied") && x.AggregateId == ticket.Id).ToArrayAsync();
            Assert.Equal(2, events.Length);
            Assert.Contains(events, x => x.Payload.Contains("resolved", StringComparison.Ordinal));
            Assert.All(events, x => Assert.DoesNotContain("Risolto fixture", x.Payload));
            Assert.All(events, x => Assert.DoesNotContain("Il problema è ancora presente", x.Payload));
        }
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/api/support/{ticket.Id}",new SupportEdit(1,"closed","Stale"))).StatusCode);
        var conversation = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{ticket.Id}/messages");
        var messages = conversation.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(3,messages.Length);
        Assert.Contains(messages,m=>m.GetProperty("body").GetString()=="Risolto fixture");
        Assert.Contains(messages,m=>m.GetProperty("toState").GetString()=="resolved");
        Assert.True(conversation.GetProperty("canReply").GetBoolean());
        Assert.Equal(5,conversation.GetProperty("version").GetInt64());
        Assert.Equal(new[] { "resolved", "open", "in_progress", "closed" }, conversation.GetProperty("allowedStates").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/support/bulk-queue", new { queueId = (Guid?)null, tickets = new[] { new { ticketId = ticket.Id, expectedVersion = 5L } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/support/bulk-queue", new { queueId = (Guid?)null, tickets = new[] { new { ticketId = ticket.Id, expectedVersion = 5L } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/support-queues/{siteQueueId}", new { name = "Rete", expectedName = "Rete", expectedVersion = 2, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/support/{ticket.Id}", new { expectedVersion = 6, state = "resolved", resolution = "Risolto fixture", queueId = siteQueueId, updateQueue = true })).StatusCode);
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
        Auth();
        var requesterView = await agent.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/agent/support/{owner}/{ticket.Id}/messages");
        Assert.DoesNotContain(requesterView.GetProperty("messages").EnumerateArray(), m => m.GetProperty("body").GetString() == "Nota interna solo per assistenza");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = await db.AuditEvents.SingleAsync(x => x.Action == "ticket.note.internal.created" && x.Target == ticket.Id.ToString());
            Assert.Equal(adminUser, audit.ActorId);
            var evt = await db.CoreOutboxEvents.SingleAsync(x => x.EventType == "ticket.internal_note.created" && x.AggregateId == ticket.Id);
            Assert.DoesNotContain("Nota interna solo per assistenza", evt.Payload);
        }
        var manual = new ManualSupportCreate(Guid.NewGuid(), device, "Intervento telefonico", "Segnalazione raccolta dall'operatore", "network", "p2");
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync("/api/support/manual", manual with { DeviceId = Guid.NewGuid() })).StatusCode);
        var scopedManual = await scoped.PostAsJsonAsync("/api/support/manual", manual with { RequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, scopedManual.StatusCode);
        Assert.Equal("operator", (await scopedManual.Content.ReadFromJsonAsync<SupportTicketView>())!.Origin);
        var manualResponse = await admin.PostAsJsonAsync("/api/support/manual", manual);
        Assert.Equal(HttpStatusCode.Created, manualResponse.StatusCode);
        var manualTicket = (await manualResponse.Content.ReadFromJsonAsync<SupportTicketView>())!;
        Assert.Equal("operator", manualTicket.Origin);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.SupportTickets.SingleAsync(x => x.Id == manualTicket.Id);
            Assert.Equal(siteId, saved.SiteId);
            Assert.Equal("p2", saved.Priority);
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/support/manual", manual)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/support/manual", manual with { Priority = "high" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/support/manual", manual with { Subject = "Richiesta diversa" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/support/manual", manual with { Priority = "emergency" })).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support?deviceId={device}"))!, row => row.GetProperty("id").GetGuid() == manualTicket.Id && row.GetProperty("origin").GetString() == "operator");
        var unrelatedDevice = Guid.NewGuid();
        var sameSiteUnmanagedDevice = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Devices.Add(new Device { Id = unrelatedDevice, Name = "Dispositivo non autorizzato", SiteId = otherSiteId });
            db.Devices.Add(new Device { Id = sameSiteUnmanagedDevice, Name = "Dispositivo non gestibile", SiteId = siteId });
            await db.SaveChangesAsync();
        }
        var primaryLinks = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{manualTicket.Id}/devices");
        Assert.Single(primaryLinks!);
        Assert.True(primaryLinks![0].GetProperty("isPrimary").GetBoolean());
        Assert.DoesNotContain(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{manualTicket.Id}/linkable-devices"), row => row.GetProperty("deviceId").GetGuid() == unrelatedDevice);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/support/{manualTicket.Id}/devices", new { expectedVersion = 1L, deviceId = unrelatedDevice })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/support/{manualTicket.Id}/devices", new { expectedVersion = 1L, deviceId = sameSiteUnmanagedDevice })).StatusCode);
        var linked = new { expectedVersion = 1L, deviceId = sameSiteUnmanagedDevice };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support/{manualTicket.Id}/devices", linked)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support/{manualTicket.Id}/devices", linked)).StatusCode);
        var allLinks = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{manualTicket.Id}/devices");
        Assert.Equal(2, allLinks!.Length);
        Assert.Contains(allLinks, row => row.GetProperty("deviceId").GetGuid() == sameSiteUnmanagedDevice && !row.GetProperty("isPrimary").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/support/{manualTicket.Id}/devices/{device}?expectedVersion=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/support/{manualTicket.Id}/devices/{sameSiteUnmanagedDevice}?expectedVersion=2")).StatusCode);
        Assert.Single(await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/support/{manualTicket.Id}/devices"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 3, state = "resolved", resolution = "Risolto per prova workflow", addReply = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 4, state = "open", resolution = (string?)null })).StatusCode);
        var reopened = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{manualTicket.Id}/messages");
        Assert.Equal("open", reopened.GetProperty("state").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reopened.GetProperty("resolution").ValueKind);
        Assert.Equal(new[] { "open", "in_progress", "resolved" }, reopened.GetProperty("allowedStates").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 5, state = "closed", resolution = "Non saltare la risoluzione" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 5, state = "in_progress", resolution = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 6, state = "resolved", resolution = "Seconda risoluzione", addReply = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 7, state = "closed", resolution = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/support/{manualTicket.Id}", new
        { expectedVersion = 8, state = "open", resolution = (string?)null })).StatusCode);
        var closed = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{manualTicket.Id}/messages");
        Assert.Equal("closed", closed.GetProperty("state").GetString());
        Assert.Equal(new[] { "closed" }, closed.GetProperty("allowedStates").EnumerateArray().Select(x => x.GetString()));

        var definitionV1 = TicketWorkflowPolicyTests.CustomDefinition();
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync("/api/admin/support-workflows")).StatusCode);
        var workflowCreate = await admin.PostAsJsonAsync("/api/admin/support-workflows", new { name = "Assistenza sede", siteId, definition = definitionV1 });
        Assert.Equal(HttpStatusCode.Created, workflowCreate.StatusCode);
        var workflowId = (await workflowCreate.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync("/api/admin/support-workflows", new { name = "Non autorizzato", siteId, definition = definitionV1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/support-workflows", new { name = "Duplicato sede", siteId, definition = definitionV1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/support-workflows/{workflowId}/publish?expectedVersion=1", null)).StatusCode);
        var customTicketRequest = new ManualSupportCreate(Guid.NewGuid(), device, "Fornitore non raggiungibile", "Test workflow personalizzato", "network", "normal");
        var customTicketResponse = await admin.PostAsJsonAsync("/api/support/manual", customTicketRequest);
        Assert.Equal(HttpStatusCode.Created, customTicketResponse.StatusCode);
        var customTicketId = (await customTicketResponse.Content.ReadFromJsonAsync<SupportTicketView>())!.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bound = await db.SupportTickets.SingleAsync(x => x.Id == customTicketId);
            Assert.Equal(workflowId, bound.WorkflowId);
            Assert.Equal(1, bound.WorkflowVersion);
        }
        var customThread = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/support/{customTicketId}/messages");
        Assert.Equal(1, customThread.GetProperty("workflowVersion").GetInt32());
        Assert.Equal("In attesa fornitore", customThread.GetProperty("stateLabels").GetProperty("waiting_vendor").GetString());
        Assert.Contains("waiting_vendor", customThread.GetProperty("allowedStates").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/support/{customTicketId}", new
        { expectedVersion = 1, state = "closed", resolution = "Non saltare il flusso", addReply = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{customTicketId}", new
        { expectedVersion = 1, state = "waiting_vendor", resolution = (string?)null })).StatusCode);

        var definitionV2 = definitionV1 with
        {
            States = [.. definitionV1.States, new TicketWorkflowState("on_hold", "In attesa interna")],
            Transitions = [.. definitionV1.Transitions.Where(x => !(x.From == "waiting_vendor" && x.To == "resolved")),
                new TicketWorkflowTransition("on_hold", "on_hold"), new TicketWorkflowTransition("in_progress", "on_hold")]
        };
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/support-workflows/{workflowId}",
            new TicketWorkflowDraftRequest(2, "Assistenza sede", definitionV2))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/support-workflows/{workflowId}/publish?expectedVersion=3", null)).StatusCode);
        var newCustomTicketResponse = await admin.PostAsJsonAsync("/api/support/manual", customTicketRequest with { RequestId = Guid.NewGuid(), Subject = "Ticket dopo la pubblicazione" });
        Assert.Equal(HttpStatusCode.Created, newCustomTicketResponse.StatusCode);
        var newCustomTicketId = (await newCustomTicketResponse.Content.ReadFromJsonAsync<SupportTicketView>())!.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldBinding = await db.SupportTickets.SingleAsync(x => x.Id == customTicketId);
            var newBinding = await db.SupportTickets.SingleAsync(x => x.Id == newCustomTicketId);
            Assert.Equal(1, oldBinding.WorkflowVersion);
            Assert.Equal(2, newBinding.WorkflowVersion);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{customTicketId}", new
        { expectedVersion = 2, state = "resolved", resolution = "Il fornitore ha ripristinato il servizio", addReply = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/support-workflows/{workflowId}/deactivate?expectedVersion=4", null)).StatusCode);

        var globalWorkflowResponse = await admin.PostAsJsonAsync("/api/admin/support-workflows", new
        { name = "Workflow globale", siteId = (Guid?)null, definition = definitionV1 });
        Assert.Equal(HttpStatusCode.Created, globalWorkflowResponse.StatusCode);
        var globalWorkflowId = (await globalWorkflowResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/support-workflows/{globalWorkflowId}/publish?expectedVersion=1", null)).StatusCode);
        var fallbackTicketResponse = await admin.PostAsJsonAsync("/api/support/manual", customTicketRequest with { RequestId = Guid.NewGuid(), Subject = "Ticket workflow globale" });
        Assert.Equal(HttpStatusCode.Created, fallbackTicketResponse.StatusCode);
        var fallbackTicketId = (await fallbackTicketResponse.Content.ReadFromJsonAsync<SupportTicketView>())!.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fallback = await db.SupportTickets.SingleAsync(x => x.Id == fallbackTicketId);
            Assert.Equal(globalWorkflowId, fallback.WorkflowId);
            Assert.Equal(1, fallback.WorkflowVersion);
        }
        var summaryTicketResponse = await admin.PostAsJsonAsync("/api/support/manual", customTicketRequest with
        { RequestId = Guid.NewGuid(), Subject = "Riepilogo intervento" });
        Assert.Equal(HttpStatusCode.Created, summaryTicketResponse.StatusCode);
        var summaryTicketId = (await summaryTicketResponse.Content.ReadFromJsonAsync<SupportTicketView>())!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support/{summaryTicketId}/interventions",
            new { expectedVersion = 1L, deviceId = device, artifactType = "remote_session", artifactId = remoteSessionId })).StatusCode);
        var summariesUrl = $"/api/support/{summaryTicketId}/intervention-summaries";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(summariesUrl, new
        { expectedVersion = 2L, deviceId = device, artifactType = "remote_session", artifactId = remoteSessionId,
            summary = " ", outcome = "follow_up", followUp = (string?)null })).StatusCode);
        var followUpSummary = new { expectedVersion = 2L, deviceId = device, artifactType = "remote_session", artifactId = remoteSessionId,
            summary = "Verificato il collegamento e riavviata la sessione autorizzata.", outcome = "follow_up", followUp = "Confermare con il richiedente che l'accesso sia stabile." };
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(summariesUrl, followUpSummary)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(summariesUrl, followUpSummary)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scoped.GetAsync(summariesUrl)).StatusCode);
        Assert.Empty((await scoped.GetFromJsonAsync<System.Text.Json.JsonElement[]>(summariesUrl))!);
        var summaryHistory = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>(summariesUrl);
        Assert.Single(summaryHistory!);
        Assert.Equal("follow_up", summaryHistory![0].GetProperty("outcome").GetString());
        Assert.Equal("Confermare con il richiedente che l'accesso sia stabile.", summaryHistory[0].GetProperty("followUp").GetString());
        Assert.Equal("remote_session", summaryHistory[0].GetProperty("artifactType").GetString());
        Assert.True(summaryHistory[0].GetProperty("recordedAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(summariesUrl, new
        { expectedVersion = 3L, deviceId = device, artifactType = "remote_session", artifactId = remoteSessionId,
            summary = "Nessun nuovo errore rilevato.", outcome = "no_change", followUp = (string?)null })).StatusCode);
        summaryHistory = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>(summariesUrl);
        Assert.Equal(2, summaryHistory!.Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync(summariesUrl, new
        { expectedVersion = 4L, deviceId = device, artifactType = "remote_session", artifactId = remoteSessionId,
            summary = "Tentativo senza capability RMM", outcome = "completed", followUp = (string?)null })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.SupportInterventionSummaries.Where(x => x.TicketId == summaryTicketId).ToArrayAsync();
            Assert.Equal(2, saved.Length);
            Assert.All(saved, x => Assert.Equal(remoteSessionId, x.ArtifactId));
            var audits = await db.AuditEvents.Where(x => x.Action == "ticket.intervention.summary.created" && x.Target == summaryTicketId.ToString()).ToArrayAsync();
            Assert.Equal(2, audits.Length);
            Assert.All(audits, x => Assert.Equal(adminUser, x.ActorId));
            var events = await db.CoreOutboxEvents.Where(x => x.EventType == "ticket.intervention.summary.created" && x.AggregateId == summaryTicketId).ToArrayAsync();
            Assert.Equal(2, events.Length);
            Assert.All(events, evt => Assert.DoesNotContain("Verificato il collegamento", evt.Payload));
            Assert.All(events, evt => Assert.DoesNotContain("Confermare con il richiedente", evt.Payload));
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RemoteSessions.RemoveRange(db.RemoteSessions.Where(x => x.Id == remoteSessionId));
            db.UpdateJobs.RemoveRange(db.UpdateJobs.Where(x => x.Id == updateJobId));
            await db.SaveChangesAsync();
        }
    }
}
