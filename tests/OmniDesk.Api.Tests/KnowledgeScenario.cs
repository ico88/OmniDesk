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
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/knowledge/scopes")).StatusCode);
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

        var siteResponse = await admin.PostAsJsonAsync("/api/admin/sites", new { name = "Knowledge site " + Guid.NewGuid().ToString("N")[..8] });
        Assert.Equal(HttpStatusCode.Created, siteResponse.StatusCode);
        var siteId = (await siteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var groupResponse = await admin.PostAsJsonAsync("/api/admin/groups", new { name = "Knowledge group " + Guid.NewGuid().ToString("N")[..8] });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var groupId = (await groupResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var siteSlug = "site-guide-" + Guid.NewGuid().ToString("N")[..8];
        var groupSlug = "group-guide-" + Guid.NewGuid().ToString("N")[..8];
        var siteArticle = await admin.PostAsJsonAsync("/api/knowledge", new { slug = siteSlug, title = "Site guide", summary = "Scoped to site", body = "Site content", isPublished = true, siteId });
        var groupArticle = await admin.PostAsJsonAsync("/api/knowledge", new { slug = groupSlug, title = "Group guide", summary = "Scoped to group", body = "Group content", isPublished = true, groupId });
        Assert.Equal(HttpStatusCode.Created, siteArticle.StatusCode);
        Assert.Equal(HttpStatusCode.Created, groupArticle.StatusCode);
        var siteArticleId = (await siteArticle.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var groupArticleId = (await groupArticle.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(siteId, (await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{siteArticleId}/versions/1")).GetProperty("siteId").GetGuid());
        Assert.Equal(groupId, (await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{groupArticleId}/versions/1")).GetProperty("groupId").GetGuid());

        var scopedEmail = $"knowledge-scoped-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/users", new { email = scopedEmail, password, role = "Viewer" })).StatusCode);
        using var scopedClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Csrf(scopedClient);
        Assert.Equal(HttpStatusCode.NoContent, (await scopedClient.PostAsJsonAsync("/api/auth/login", new { email = scopedEmail, password })).StatusCode);
        await Csrf(scopedClient);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = (await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/users"))!.Single(x => x.GetProperty("email").GetString() == scopedEmail).GetProperty("id").GetString(), role = "Operator", siteId, groupId = (Guid?)null, deviceId = (Guid?)null })).StatusCode);
        var scopedArticles = await scopedClient.GetFromJsonAsync<JsonElement[]>("/api/knowledge");
        Assert.Contains(scopedArticles!, x => x.GetProperty("id").GetGuid() == siteArticleId);
        Assert.DoesNotContain(scopedArticles!, x => x.GetProperty("id").GetGuid() == groupArticleId || x.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.OK, (await scopedClient.GetAsync($"/api/knowledge/{siteArticleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scopedClient.GetAsync($"/api/knowledge/{groupArticleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scopedClient.GetAsync($"/api/knowledge/{siteArticleId}/versions")).StatusCode);

        var legacyUpdate = await admin.PutAsJsonAsync($"/api/knowledge/{siteArticleId}", new { slug = siteSlug, title = "Site guide", summary = "Legacy client update", body = "Scoped content", isPublished = true, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, legacyUpdate.StatusCode);
        Assert.Equal(siteId, (await legacyUpdate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("siteId").GetGuid());
        var moved = await admin.PutAsJsonAsync($"/api/knowledge/{siteArticleId}", new { slug = siteSlug, title = "Site guide", summary = "Moved to group", body = "Scoped content", isPublished = true, expectedVersion = 2, siteId = (Guid?)null, groupId, scopeSpecified = true });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var historicalScope = await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{siteArticleId}/versions/1");
        Assert.Equal(siteId, historicalScope.GetProperty("siteId").GetGuid());
        Assert.Equal(siteId, (await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{siteArticleId}/versions/2")).GetProperty("siteId").GetGuid());
        Assert.Equal(groupId, (await admin.GetFromJsonAsync<JsonElement>($"/api/knowledge/{siteArticleId}/versions/3")).GetProperty("groupId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await scopedClient.GetAsync($"/api/knowledge/{siteArticleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = (await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/users"))!.Single(x => x.GetProperty("email").GetString() == scopedEmail).GetProperty("id").GetString(), role = "Operator", siteId = (Guid?)null, groupId, deviceId = (Guid?)null })).StatusCode);
        var groupScopedArticles = await scopedClient.GetFromJsonAsync<JsonElement[]>("/api/knowledge");
        Assert.Contains(groupScopedArticles!, x => x.GetProperty("id").GetGuid() == groupArticleId);
        Assert.Contains(groupScopedArticles!, x => x.GetProperty("id").GetGuid() == siteArticleId);

        var invalidScope = await admin.PostAsJsonAsync("/api/knowledge", new { slug = "invalid-scope-" + Guid.NewGuid().ToString("N")[..8], title = "Invalid scope", summary = "", body = "No", isPublished = true, siteId, groupId });
        Assert.Equal(HttpStatusCode.BadRequest, invalidScope.StatusCode);

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
