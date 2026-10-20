using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.ServiceDesk;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class EmailIntakeScenario(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Password = "EmailIntake!9-test-password";
    private readonly string email = "email-intake-" + Guid.NewGuid().ToString("N") + "@example.test";
    private string userId = "";

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestDatabaseMigration.ApplyAsync(db);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { Email = email, UserName = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded);
        userId = user.Id;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task AddCsrf(HttpClient client)
    {
        var value = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("token").GetString());
    }

    private async Task Login(HttpClient client)
    {
        await AddCsrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        await AddCsrf(client);
    }

    private async Task<HttpResponseMessage> PostSigned(HttpClient client, byte[] body, long? timestamp = null, bool tamper = false)
    {
        File.WriteAllBytes(factory.EmailIntakeSigningKeyFile, factory.EmailIntakeSigningKey);
        var sentAt = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = EmailIntakeSignature.Sign(factory.EmailIntakeSigningKey, long.Parse(sentAt), body);
        if (tamper) signature = signature[..^1] + (signature[^1] == '0' ? "1" : "0");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/email/intake")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-OmniDesk-Mail-Timestamp", sentAt);
        request.Headers.Add("X-OmniDesk-Mail-Signature", signature);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Signed_email_is_staged_once_and_ticket_managers_can_review_or_discard_it()
    {
        using var anonymous = Client();
        var messageId = "<" + Guid.NewGuid().ToString("N") + "@sender.example>";
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            messageId,
            mailbox = "Support@Example.Test",
            sender = "requester@example.net",
            subject = "Cannot sign in",
            textBody = "The sign-in page returns an error.",
            receivedAt = DateTimeOffset.UtcNow
        });
        using var invalidSignature = await PostSigned(anonymous, body, tamper: true);
        Assert.True(invalidSignature.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected invalid HMAC to be rejected, got {(int)invalidSignature.StatusCode}: {await invalidSignature.Content.ReadAsStringAsync()}");
        using var stale = await PostSigned(anonymous, body, DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds());
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        using var accepted = await PostSigned(anonymous, body);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var acceptedResult = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        var id = acceptedResult.GetProperty("id").GetGuid();
        Assert.Equal("pending", acceptedResult.GetProperty("state").GetString());

        using var replay = await PostSigned(anonymous, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());

        var changedContent = JsonSerializer.SerializeToUtf8Bytes(new
        {
            messageId,
            mailbox = "support@example.test",
            sender = "requester@example.net",
            subject = "Changed body with the same Message-ID",
            textBody = "This must not overwrite the accepted message.",
            receivedAt = DateTimeOffset.UtcNow
        });
        using var conflictingReplay = await PostSigned(anonymous, changedContent);
        Assert.Equal(HttpStatusCode.Conflict, conflictingReplay.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/email-intakes")).StatusCode);
        using var operatorClient = Client();
        await Login(operatorClient);
        var inbox = await operatorClient.GetFromJsonAsync<JsonElement[]>("/api/admin/email-intakes");
        Assert.NotNull(inbox);
        var item = Assert.Single(inbox!, x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("requester@example.net", item.GetProperty("senderAddress").GetString());
        Assert.Equal("pending", item.GetProperty("state").GetString());

        await AddCsrf(operatorClient);
        Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.PostAsync($"/api/admin/email-intakes/{id}/discard", null)).StatusCode);
        var afterDiscard = await operatorClient.GetFromJsonAsync<JsonElement[]>("/api/admin/email-intakes");
        Assert.DoesNotContain(afterDiscard!, x => x.GetProperty("id").GetGuid() == id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var intake = await db.SupportEmailIntakes.SingleAsync(x => x.Id == id);
            Assert.Equal("discarded", intake.State);
            Assert.Equal(userId, intake.QualifiedBy);
            var audit = await db.AuditEvents.Where(x => x.Target == id.ToString()).Select(x => x.Action).ToArrayAsync();
            Assert.Contains("ticket.email.intake.received", audit);
            Assert.Contains("ticket.email.intake.discarded", audit);
            Assert.False(await db.SupportTickets.AnyAsync(x => x.Subject == "Cannot sign in" && x.Origin == "email"));
        }
    }

    [Fact]
    public void Signature_verification_rejects_short_secrets_and_expired_timestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds();
        var body = "{}"u8.ToArray();
        var signature = EmailIntakeSignature.Sign(factory.EmailIntakeSigningKey, timestamp, body);
        Assert.True(EmailIntakeSignature.Verify(factory.EmailIntakeSigningKey, timestamp.ToString(), signature, body, now, out _));
        Assert.False(EmailIntakeSignature.Verify(RandomNumberGenerator.GetBytes(31), timestamp.ToString(), signature, body, now, out _));
        Assert.False(EmailIntakeSignature.Verify(factory.EmailIntakeSigningKey, timestamp.ToString(), signature, body, now.AddMinutes(6), out _));
        Assert.False(EmailIntakeSignature.Verify(factory.EmailIntakeSigningKey, long.MinValue.ToString(), signature, body, now, out _));
        Assert.False(EmailIntakeSignature.Verify(factory.EmailIntakeSigningKey, long.MaxValue.ToString(), signature, body, now, out _));
    }
}
