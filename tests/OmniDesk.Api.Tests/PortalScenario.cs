using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support/{ticketId}/internal-notes", new { expectedVersion = 1L, body = "Nota riservata al team" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/support/{ticketId}", new { expectedVersion = 2L, state = "in_progress", resolution = "Stiamo verificando il collegamento." , addReply = true })).StatusCode);
        var detail = await portal.GetFromJsonAsync<JsonElement>($"/api/portal/tickets/{ticketId}");
        var messages = detail.GetProperty("messages");
        Assert.DoesNotContain(messages.EnumerateArray(), message => message.GetProperty("body").GetString() == "Nota riservata al team");
        Assert.Equal("La stampante del reparto non risponde.", messages[0].GetProperty("body").GetString());
        Assert.Contains(messages.EnumerateArray(), message => message.GetProperty("body").GetString() == "Stiamo verificando il collegamento.");
        await Csrf(portal);
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PostAsJsonAsync($"/api/portal/tickets/{ticketId}/messages", new { expectedVersion = 3L, body = "Ho verificato il cavo." })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/portal-device-access/{userId}/{device}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/tickets/{ticketId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/tickets/{ticketId}/screenshot")).StatusCode);
        await Csrf(portal);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.PostAsJsonAsync($"/api/portal/tickets/{ticketId}/messages", new { expectedVersion = 4L, body = "Non autorizzato dopo revoca" })).StatusCode);
    }
}
