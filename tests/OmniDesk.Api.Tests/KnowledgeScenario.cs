using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class KnowledgeScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient viewer, string password)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/knowledge")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/knowledge", new { slug = "viewer", title = "Forbidden", summary = "", body = "no", isPublished = true })).StatusCode);

        var slug = "first-line-guide-" + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/knowledge", new { slug, title = "First line", summary = "Safe diagnostic steps", body = "Read-only checks first.\nNever paste credentials.", isPublished = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = article.GetProperty("id").GetGuid();
        Assert.Equal(1, article.GetProperty("version").GetInt64());
        var initialVersions = await admin.GetFromJsonAsync<JsonElement[]>($"/api/knowledge/{id}/versions");
        Assert.Single(initialVersions!);
        Assert.Equal(1, initialVersions![0].GetProperty("version").GetInt64());
        Assert.False(initialVersions[0].TryGetProperty("body", out _), "Version list does not send stored article bodies.");
        Assert.Equal("Never paste credentials.", (await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{id}/versions/1")).GetProperty("body").GetString()!.Split('\n')[1]);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/knowledge/{id}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/knowledge/{id}/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/knowledge", new { slug, title = "Duplicate", summary = "", body = "duplicate", isPublished = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/knowledge", new { slug = "Not a slug", title = "Invalid", summary = "", body = "Body", isPublished = true })).StatusCode);

        using var operatorClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var operatorEmail = $"knowledge-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/users", new { email = operatorEmail, password, role = "Operator" })).StatusCode);
        await Csrf(operatorClient);
        Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.PostAsJsonAsync("/api/auth/login", new { email = operatorEmail, password })).StatusCode);
        await Csrf(operatorClient);
        var visible = await operatorClient.GetFromJsonAsync<JsonElement[]>("/api/knowledge");
        Assert.Contains(visible!, x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("Read-only checks first.\nNever paste credentials.", (await operatorClient.GetFromJsonAsync<JsonElement>($"/api/knowledge/{id}")).GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PutAsJsonAsync($"/api/knowledge/{id}", new { slug, title = "Hacked", summary = "", body = "no", isPublished = true, expectedVersion = 1 })).StatusCode);

        var draft = await admin.PostAsJsonAsync("/api/knowledge", new { slug = slug + "-draft", title = "Draft", summary = "Internal draft", body = "Do not show to readers", isPublished = false });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var draftId = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.GetAsync($"/api/knowledge/{draftId}")).StatusCode);
        Assert.DoesNotContain(await operatorClient.GetFromJsonAsync<JsonElement[]>("/api/knowledge") ?? [], x => x.GetProperty("id").GetGuid() == draftId);

        var concurrent = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/knowledge/{id}", new { slug, title = "First line update A", summary = "Updated A", body = "Step one.\nStep two.", isPublished = true, expectedVersion = 1 }),
            admin.PutAsJsonAsync($"/api/knowledge/{id}", new { slug, title = "First line update B", summary = "Updated B", body = "Step one.\nStep two.", isPublished = true, expectedVersion = 1 }));
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);
        var afterConcurrent = await admin.GetFromJsonAsync<JsonElement[]>($"/api/knowledge/{id}/versions");
        Assert.Equal([2L, 1L], afterConcurrent!.Select(x => x.GetProperty("version").GetInt64()).ToArray());
        var original = await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{id}/versions/1");
        Assert.Equal("First line", original.GetProperty("title").GetString());
        Assert.Equal("Read-only checks first.\nNever paste credentials.", original.GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/knowledge/{id}", new { slug, title = "Stale write", summary = "", body = "stale", isPublished = true, expectedVersion = 1 })).StatusCode);
        var unpublish = await admin.PutAsJsonAsync($"/api/knowledge/{id}", new { slug, title = "First line updated", summary = "Updated", body = "Step one.\nStep two.", isPublished = false, expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        var afterUnpublish = await admin.GetFromJsonAsync<JsonElement[]>($"/api/knowledge/{id}/versions");
        Assert.Equal(new long[] { 3, 2, 1 }, afterUnpublish!.Select(x => x.GetProperty("version").GetInt64()).ToArray());
        Assert.Equal("First line", (await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{id}/versions/1")).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.GetAsync($"/api/knowledge/{id}")).StatusCode);
    }

    private static async Task Csrf(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }
}
