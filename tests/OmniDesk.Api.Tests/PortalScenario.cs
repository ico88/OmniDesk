using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class PortalScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, Guid device)
    {
        var password = "Portal!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var created = await admin.PostAsJsonAsync("/api/admin/users", new { email = "portal-user@example.test", password, role = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var outsiderCreated = await admin.PostAsJsonAsync("/api/admin/users", new { email = "portal-outsider@example.test", password, role = (string?)null });
        Assert.Equal(HttpStatusCode.Created, outsiderCreated.StatusCode);
        using var portal = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var outsider = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        static async Task Csrf(HttpClient client)
        {
            var value = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("token").GetString());
        }
        async Task Login(HttpClient client, string email)
        {
            await Csrf(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
            await Csrf(client);
        }
        await Login(portal, "portal-user@example.test");
        await Login(outsider, "portal-outsider@example.test");

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/portal-device-access", new { userId, deviceId = device })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/portal-device-access", new { userId, deviceId = device })).StatusCode);
        var devices = await portal.GetFromJsonAsync<JsonElement[]>("/api/portal/devices");
        Assert.Equal(device, Assert.Single(devices!).GetProperty("deviceId").GetGuid());
        Assert.Empty(await outsider.GetFromJsonAsync<JsonElement[]>("/api/portal/devices") ?? []);

        var png = SupportScenario.Screenshot();
        var request = new { requestId = Guid.NewGuid(), deviceId = device, subject = "Stampante non disponibile", description = "La stampante del reparto non risponde.", category = "hardware", screenshotBase64 = Convert.ToBase64String(png), screenshotConsent = true };
        portal.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsJsonAsync("/api/portal/tickets", request)).StatusCode);
        await Csrf(portal);
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsJsonAsync("/api/portal/tickets", new { requestId = request.requestId, deviceId = request.deviceId, subject = request.subject, description = request.description, category = request.category, screenshotBase64 = request.screenshotBase64, screenshotConsent = false })).StatusCode);
        await Csrf(portal);
        var response = await portal.PostAsJsonAsync("/api/portal/tickets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = ticket.GetProperty("id").GetGuid();
        long publicMessageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = new SupportMessage { TicketId = ticketId, Kind = "portal_reply", Body = "Riferimento allegato", Visibility = "public" };
            db.SupportMessages.Add(message);
            await db.SaveChangesAsync();
            publicMessageId = message.Id;
        }
        Assert.Equal("portal-user@example.test", (await portal.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.OK, (await portal.PostAsJsonAsync("/api/portal/tickets", request)).StatusCode);
        var image = await portal.GetAsync($"/api/portal/tickets/{ticketId}/screenshot");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("no-store", image.Headers.CacheControl?.ToString());
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/portal/tickets/{ticketId}/screenshot")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OmniDesk.Api.Data.AppDbContext>();
            var saved = await db.SupportTickets.FindAsync(ticketId);
            Assert.Equal(userId, saved!.RequesterUserId);
            Assert.Equal("portal", saved.Origin);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/portal/tickets/{ticketId}")).StatusCode);
        Assert.Empty(await outsider.GetFromJsonAsync<JsonElement[]>("/api/portal/tickets") ?? []);

        var attachmentPath = $"/api/portal/tickets/{ticketId}/attachments";
        Assert.Empty(await portal.GetFromJsonAsync<JsonElement[]>(attachmentPath) ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(attachmentPath)).StatusCode);
        using (var missingConsent = new MultipartFormDataContent())
        {
            missingConsent.Add(new StringContent("1"), "expectedVersion");
            missingConsent.Add(new StringContent("false"), "consent");
            missingConsent.Add(new ByteArrayContent("portal attachment"u8.ToArray()), "file", "manual.txt");
            Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsync(attachmentPath, missingConsent)).StatusCode);
        }
        await Csrf(portal);
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("1"), "expectedVersion");
            form.Add(new StringContent("true"), "consent");
            form.Add(new StringContent(publicMessageId.ToString()), "messageId");
            var content = new ByteArrayContent("portal attachment clean fixture"u8.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(content, "file", "manual.txt");
            var uploaded = await portal.PostAsync(attachmentPath, form);
            Assert.Equal(HttpStatusCode.Accepted, uploaded.StatusCode);
            var attachment = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
            var attachmentId = attachment.GetProperty("id").GetGuid();
            Assert.Equal("quarantined", attachment.GetProperty("scanState").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"{attachmentPath}/{attachmentId}")).StatusCode);
            var listed = Assert.Single(await portal.GetFromJsonAsync<JsonElement[]>(attachmentPath) ?? []);
            Assert.Equal("manual.txt", listed.GetProperty("fileName").GetString());
            Assert.Equal(publicMessageId, listed.GetProperty("messageId").GetInt64());
            var ticketDetail = await portal.GetFromJsonAsync<JsonElement>($"/api/portal/tickets/{ticketId}");
            var linked = ticketDetail.GetProperty("messages").EnumerateArray().Single(message => message.GetProperty("id").GetInt64() == publicMessageId);
            Assert.Equal("manual.txt", Assert.Single(linked.GetProperty("attachments").EnumerateArray()).GetProperty("fileName").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{attachmentPath}/{attachmentId}")).StatusCode);
        }

        var emailOptions = Options.Create(new OutboundEmailOptions
        {
            Enabled = true, Host = "smtp.example.test", Port = 587, FromAddress = "help@example.test",
            Username = "omni", PasswordFile = "/tmp/not-used-smtp-secret", DashboardUrl = "https://desk.example.test"
        });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var requester = await db.Users.SingleAsync(user => user.Id == userId);
            requester.EmailConfirmed = true;
            requester.EmailNotificationsEnabled = true;
            requester.EmailNotificationsEnabledAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support/{ticketId}/internal-notes", new { expectedVersion = 2L, body = "Nota riservata al team" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{ticketId}", new { expectedVersion = 3L, state = "in_progress", resolution = "Stiamo verificando il collegamento." , addReply = true })).StatusCode);
        var ticketNotificationWorker = new TicketNotificationWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TicketNotificationWorker>.Instance, emailOptions);
        while (await ticketNotificationWorker.ProcessNext(CancellationToken.None)) { }
        var portalInbox = await portal.GetFromJsonAsync<JsonElement>("/api/notifications");
        var requesterNotice = Assert.Single(portalInbox.GetProperty("notifications").EnumerateArray(), item =>
            item.TryGetProperty("ticketId", out var target) && target.GetGuid() == ticketId && item.GetProperty("kind").GetString() == "ticket.staff.reply");
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PostAsync($"/api/notifications/{requesterNotice.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsync($"/api/notifications/{requesterNotice.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        var emailWorker = new EmailNotificationWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmailNotificationWorker>.Instance, emailOptions);
        while (await emailWorker.ProcessNext(CancellationToken.None)) { }
        var requesterEmail = Assert.Single(factory.OutboundEmail.Sent, message => message.Recipient == "portal-user@example.test" && message.Subject.Contains($"#{ticket.GetProperty("number").GetInt64()}"));
        Assert.Contains("Nuova risposta dell'assistenza al ticket", requesterEmail.Subject);
        Assert.DoesNotContain("Stiamo verificando il collegamento", requesterEmail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Nota riservata al team", requesterEmail.TextBody, StringComparison.Ordinal);
        var detail = await portal.GetFromJsonAsync<JsonElement>($"/api/portal/tickets/{ticketId}");
        var messages = detail.GetProperty("messages");
        Assert.DoesNotContain(messages.EnumerateArray(), message => message.GetProperty("body").GetString() == "Nota riservata al team");
        Assert.Equal("La stampante del reparto non risponde.", messages[0].GetProperty("body").GetString());
        Assert.Contains(messages.EnumerateArray(), message => message.GetProperty("body").GetString() == "Stiamo verificando il collegamento.");
        await Csrf(portal);
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PostAsJsonAsync($"/api/portal/tickets/{ticketId}/messages", new { expectedVersion = 4L, body = "Ho verificato il cavo." })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/portal-device-access/{userId}/{device}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/tickets/{ticketId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/tickets/{ticketId}/screenshot")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(attachmentPath)).StatusCode);
        portalInbox = await portal.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.DoesNotContain(portalInbox.GetProperty("notifications").EnumerateArray(), item => item.GetProperty("id").GetGuid() == requesterNotice.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.PostAsync($"/api/notifications/{requesterNotice.GetProperty("id").GetGuid()}/read", null)).StatusCode);
        await Csrf(portal);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.PostAsJsonAsync($"/api/portal/tickets/{ticketId}/messages", new { expectedVersion = 4L, body = "Non autorizzato dopo revoca" })).StatusCode);
    }
}
