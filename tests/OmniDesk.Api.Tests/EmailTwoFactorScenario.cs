using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class EmailTwoFactorScenario
{
    public static async Task Run(ApiFactory factory)
    {
        const string email = "email-mfa@example.test";
        var password = "Email!Mfa9" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { Email = email, UserName = email };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            userId = user.Id;
        }

        using var challengeHost = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:TotpEnabled"] = "false",
                ["Security:EmailTwoFactorEnabled"] = "true"
            })));
        using var client = challengeHost.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await AddCsrf(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        var challenge = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = challenge.GetProperty("challengeToken").GetString()!;
        Assert.Equal("e***@example.test", challenge.GetProperty("maskedEmail").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var sent = Assert.Single(factory.OutboundEmail.Sent, message => message.Subject == "Codice di accesso OmniDesk");
        var codeMatch = System.Text.RegularExpressions.Regex.Match(sent.TextBody, @"(?<!\d)\d{6}(?!\d)");
        Assert.True(codeMatch.Success, "The test SMTP sender did not receive a six-digit login code.");
        var code = codeMatch.Value;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.EmailLoginChallenges.SingleAsync(row => row.UserId == userId);
            Assert.DoesNotContain(code, stored.CodeHash);
            Assert.DoesNotContain(token, stored.TokenHash);
            Assert.Null(stored.UsedAt);
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login/email-code", new { challengeToken = token, code = code == "000000" ? "000001" : "000000" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.EmailLoginChallenges.SingleAsync(row => row.UserId == userId);
            stored.LastSentAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/login/email-code/resend", new { challengeToken = token })).StatusCode);
        var resent = Assert.Single(factory.OutboundEmail.Sent, message => message.Subject == "Nuovo codice di accesso OmniDesk");
        var resentMatch = System.Text.RegularExpressions.Regex.Match(resent.TextBody, @"(?<!\d)\d{6}(?!\d)");
        Assert.True(resentMatch.Success, "The test SMTP sender did not receive the replacement code.");
        var replacementCode = resentMatch.Value;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.EmailLoginChallenges.SingleAsync(row => row.UserId == userId);
            Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(9));
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login/email-code", new { challengeToken = token, code })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/login/email-code", new { challengeToken = token, code = replacementCode })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        await AddCsrf(client);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login/email-code", new { challengeToken = token, code })).StatusCode);
    }

    private static async Task AddCsrf(HttpClient client)
    {
        var value = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("token").GetString());
    }
}
