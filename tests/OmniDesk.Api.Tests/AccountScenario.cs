using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using OmniDesk.Api.Data;
using OtpNet;
using Xunit;
namespace OmniDesk.Api.Tests;
public static class AccountScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient viewer, string password)
    {
        var emailPreference = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.False(emailPreference.GetProperty("emailNotificationsEnabled").GetBoolean());
        Assert.True(emailPreference.GetProperty("emailNotificationsAvailable").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/auth/email-notifications", new { enabled = true })).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("emailNotificationsEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/auth/email-notifications", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/dashboard/layout", new { widgets = new[] { "authentication", "devices" } })).StatusCode);
        var mine = await admin.GetFromJsonAsync<JsonElement>("/api/dashboard/layout");
        Assert.Equal("authentication", mine.GetProperty("widgets")[0].GetString());
        var theirs = await viewer.GetFromJsonAsync<JsonElement>("/api/dashboard/layout"); Assert.Equal(3, theirs.GetProperty("widgets").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/dashboard/layout", new { widgets = new[] { "unknown" } })).StatusCode);
        var setup = await admin.PostAsJsonAsync("/api/auth/totp/setup", new { password }); Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var key = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedKey").GetString()!;
        string Code() => new Totp(Base32Encoding.ToBytes(key)).ComputeTotp();
        var enabled = await admin.PostAsJsonAsync("/api/auth/totp/enable", new { password, code = Code() }); Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var initialCodes = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes");
        var recovery = initialCodes[0].GetString()!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var values = await db.UserTokens.Select(x => x.Value).ToListAsync();
            Assert.NotEmpty(values); Assert.DoesNotContain(key, values); Assert.DoesNotContain(values, x => x?.Contains(recovery) == true);
        }
        async Task<HttpStatusCode> Login(string? code = null, string? recoveryCode = null, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? host = null)
        {
            using var client = (host ?? factory).CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            return (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password, code, recoveryCode })).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Login());
        Assert.Equal(HttpStatusCode.Unauthorized, await Login("invalid"));
        Assert.Equal(HttpStatusCode.NoContent, await Login(Code()));
        Assert.Equal(HttpStatusCode.NoContent, await Login(recoveryCode:recovery));
        Assert.Equal(HttpStatusCode.Unauthorized, await Login(recoveryCode:recovery));
        string? previousStamp;
        using (var scope = factory.Services.CreateScope())
            previousStamp = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(x => x.Email == "admin@example.test").Select(x => x.SecurityStamp).SingleAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/auth/totp/recovery-codes", new { password, code = "invalid" })).StatusCode);
        var rotated = await admin.PostAsJsonAsync("/api/auth/totp/recovery-codes", new { password, code = Code() });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newRecovery = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")[0].GetString()!;
        var unusedOldRecovery = initialCodes[1].GetString()!;
        // Separate host keeps these checks independent of the scenario's login rate-limit budget.
        using (var recoveryHost = factory.WithWebHostBuilder(_ => { }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await Login(recoveryCode:unusedOldRecovery, host:recoveryHost));
            Assert.Equal(HttpStatusCode.NoContent, await Login(recoveryCode:newRecovery, host:recoveryHost));
            Assert.Equal(HttpStatusCode.Unauthorized, await Login(recoveryCode:newRecovery, host:recoveryHost));
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEqual(previousStamp, await db.Users.Where(x => x.Email == "admin@example.test").Select(x => x.SecurityStamp).SingleAsync());
            Assert.True(await db.AuditEvents.AnyAsync(x => x.Action == "auth.totp.recovery.rotate"));
        }
        using (var disabledFactory = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> { ["Security:TotpEnabled"] = "false" }))))
        using (var disabled = disabledFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
        {
            var csrf = await disabled.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); disabled.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password, code = Code() })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/auth/totp/disable", new { password, code = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/auth/totp/disable", new { password, code = Code() })).StatusCode);
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me"); Assert.False(me.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsJsonAsync("/api/auth/password", new { currentPassword = password, newPassword = password + "New!" })).StatusCode);
    }
}
