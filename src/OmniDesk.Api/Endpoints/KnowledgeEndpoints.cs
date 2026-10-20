using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public sealed record KnowledgeArticleCreateRequest(string Slug, string Title, string Summary, string Body, bool IsPublished, Guid? SiteId = null, Guid? GroupId = null);
public sealed record KnowledgeArticleUpdateRequest(string Slug, string Title, string Summary, string Body, bool IsPublished, long ExpectedVersion, Guid? SiteId = null, Guid? GroupId = null, bool ScopeSpecified = false);

public static partial class KnowledgeEndpoints
{
    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    public static void MapKnowledgeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/knowledge").RequireAuthorization();
        api.MapGet("/scopes", async (HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (!await permissions.Allows(c.User.UserId(), "knowledge.manage")) return Results.Forbid();
            var sites = await db.Sites.AsNoTracking().Where(x => !x.IsArchived).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
            var groups = await db.DeviceGroups.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
            return Results.Ok(new { sites, groups });
        });
        api.MapGet("/search", async (string? q, HttpContext c, AppDbContext db, PermissionService permissions) => {
            var term = q?.Trim();
            if (term is null || term.Length is < 2 or > 100) return Results.BadRequest(new { error = "La ricerca deve contenere da 2 a 100 caratteri." });
            var userId = c.User.UserId();
            var canManage = await permissions.Allows(userId, "knowledge.manage");
            var canViewGlobally = await permissions.Allows(userId, "knowledge.view");
            var allowedSiteIds = new HashSet<Guid>();
            var allowedGroupIds = new HashSet<Guid>();
            if (!canManage && !canViewGlobally)
            {
                var scopedPermissions = await (from grant in db.ScopeGrants
                    join rolePermission in db.RolePermissions on grant.RoleId equals rolePermission.RoleId
                    where grant.UserId == userId && rolePermission.Permission == "knowledge.view" && (grant.SiteId != null || grant.GroupId != null)
                    select new { grant.SiteId, grant.GroupId }).ToListAsync();
                allowedSiteIds.UnionWith(scopedPermissions.Where(x => x.SiteId != null).Select(x => x.SiteId!.Value));
                allowedGroupIds.UnionWith(scopedPermissions.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value));
                if (allowedSiteIds.Count == 0 && allowedGroupIds.Count == 0) return Results.Forbid();
            }

            // Full-text matching is executed in PostgreSQL against the generated GIN-indexed vector.
            // Scope and publication filters are composed on the same query before results are returned.
            var query = db.KnowledgeArticles
                .FromSqlInterpolated($"SELECT * FROM \"KnowledgeArticles\" WHERE \"SearchVector\" @@ plainto_tsquery('simple', {term})")
                .AsNoTracking();
            if (!canManage) query = query.Where(x => x.IsPublished);
            if (!canManage && !canViewGlobally)
                query = query.Where(x => (x.SiteId != null && allowedSiteIds.Contains(x.SiteId.Value)) ||
                                         (x.GroupId != null && allowedGroupIds.Contains(x.GroupId.Value)));
            var matches = await query
                .OrderByDescending(x => EF.Functions.ILike(x.Title, $"%{term}%"))
                .ThenByDescending(x => EF.Functions.ILike(x.Summary, $"%{term}%"))
                .ThenByDescending(x => x.UpdatedAt)
                .Take(50)
                .Select(x => new { x.Id, x.Slug, x.Title, x.Summary, x.IsPublished, x.SiteId, x.GroupId, x.Version, x.CreatedAt, x.UpdatedAt, x.PublishedAt })
                .ToListAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(matches);
        });
        api.MapGet("", async (HttpContext c, AppDbContext db, PermissionService permissions) => {
            var userId = c.User.UserId();
            var canManage = await permissions.Allows(userId, "knowledge.manage");
            var canViewGlobally = await permissions.Allows(userId, "knowledge.view");
            var allowedSiteIds = new HashSet<Guid>();
            var allowedGroupIds = new HashSet<Guid>();
            if (!canManage && !canViewGlobally)
            {
                var scopedPermissions = await (from grant in db.ScopeGrants
                    join rolePermission in db.RolePermissions on grant.RoleId equals rolePermission.RoleId
                    where grant.UserId == userId && rolePermission.Permission == "knowledge.view" && (grant.SiteId != null || grant.GroupId != null)
                    select new { grant.SiteId, grant.GroupId }).ToListAsync();
                allowedSiteIds.UnionWith(scopedPermissions.Where(x => x.SiteId != null).Select(x => x.SiteId!.Value));
                allowedGroupIds.UnionWith(scopedPermissions.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value));
                if (allowedSiteIds.Count == 0 && allowedGroupIds.Count == 0) return Results.Forbid();
            }
            var candidates = await db.KnowledgeArticles.AsNoTracking().OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Title)
                .Select(x => new { x.Id, x.Slug, x.Title, x.Summary, x.IsPublished, x.SiteId, x.GroupId, x.Version, x.CreatedAt, x.UpdatedAt, x.PublishedAt }).ToListAsync();
            var visible = candidates.Where(article => (canManage || article.IsPublished) &&
                (canManage || canViewGlobally || article.SiteId is { } site && allowedSiteIds.Contains(site) || article.GroupId is { } group && allowedGroupIds.Contains(group)));
            return Results.Ok(visible);
        });
        api.MapGet("/{id:guid}", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) => {
            var userId = c.User.UserId();
            var article = await db.KnowledgeArticles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (article is null) return Results.NotFound();
            var canManage = await permissions.Allows(userId, "knowledge.manage");
            var canView = canManage || await permissions.Allows(userId, "knowledge.view", groupId: article.GroupId, siteId: article.SiteId);
            if (!canView || (!article.IsPublished && !canManage)) return Results.NotFound();
            return Results.Ok(article);
        });
        api.MapGet("/{id:guid}/versions", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (!await permissions.Allows(c.User.UserId(), "knowledge.manage")) return Results.Forbid();
            if (!await db.KnowledgeArticles.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var versions = await db.KnowledgeArticleRevisions.AsNoTracking().Where(x => x.ArticleId == id)
                .OrderByDescending(x => x.Version)
                .Select(x => new {
                    x.Version, x.Slug, x.Title, x.Summary, x.IsPublished, x.SiteId, x.GroupId, x.PublishedAt, x.RecordedAt,
                    recordedBy = x.RecordedBy == null ? null : db.Users.Where(u => u.Id == x.RecordedBy).Select(u => u.DisplayName ?? u.Email).FirstOrDefault()
                }).ToListAsync();
            return Results.Ok(versions);
        });
        api.MapGet("/{id:guid}/versions/{version:long}", async (Guid id, long version, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (!await permissions.Allows(c.User.UserId(), "knowledge.manage")) return Results.Forbid();
            var revision = await db.KnowledgeArticleRevisions.AsNoTracking()
                .Where(x => x.ArticleId == id && x.Version == version)
                .Select(x => new {
                    x.ArticleId, x.Version, x.Slug, x.Title, x.Summary, x.Body, x.IsPublished, x.SiteId, x.GroupId, x.PublishedAt, x.RecordedAt,
                    recordedBy = x.RecordedBy == null ? null : db.Users.Where(u => u.Id == x.RecordedBy).Select(u => u.DisplayName ?? u.Email).FirstOrDefault()
                }).SingleOrDefaultAsync();
            return revision is null ? Results.NotFound() : Results.Ok(revision);
        });
        api.MapPost("", async (KnowledgeArticleCreateRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (!await permissions.Allows(c.User.UserId(), "knowledge.manage")) return Results.Forbid();
            if (!Valid(r.Slug, r.Title, r.Summary, r.Body) || !ValidScope(r.SiteId, r.GroupId)) return Results.BadRequest();
            if (!await ScopeExists(db, r.SiteId, r.GroupId)) return Results.BadRequest(new { error = "La sede o il gruppo selezionato non è disponibile." });
            var now = DateTimeOffset.UtcNow;
            var slug = r.Slug.Trim();
            var article = new KnowledgeArticle {
                Slug = slug, NormalizedSlug = slug.ToLowerInvariant(), Title = r.Title.Trim(), Summary = r.Summary.Trim(),
                Body = r.Body.Trim(), IsPublished = r.IsPublished, SiteId = r.SiteId, GroupId = r.GroupId, CreatedAt = now, UpdatedAt = now,
                PublishedAt = r.IsPublished ? now : null
            };
            db.KnowledgeArticles.Add(article);
            db.KnowledgeArticleRevisions.Add(Revision(article, c.User.UserId(), now));
            db.AuditEvents.Add(Audit(c, "knowledge.article.create", article.Id));
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(new { error = "Esiste già un articolo con questo slug." }); }
            return Results.Created($"/api/knowledge/{article.Id}", article);
        });
        api.MapPut("/{id:guid}", async (Guid id, KnowledgeArticleUpdateRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (!await permissions.Allows(c.User.UserId(), "knowledge.manage")) return Results.Forbid();
            if (!Valid(r.Slug, r.Title, r.Summary, r.Body) || r.ExpectedVersion < 1) return Results.BadRequest();
            var prior = await db.KnowledgeArticles.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Version, x.PublishedAt, x.IsPublished, x.SiteId, x.GroupId }).SingleOrDefaultAsync();
            if (prior is null) return Results.NotFound();
            var siteId = r.ScopeSpecified ? r.SiteId : prior.SiteId;
            var groupId = r.ScopeSpecified ? r.GroupId : prior.GroupId;
            if (!ValidScope(siteId, groupId)) return Results.BadRequest();
            var preservesArchivedSite = siteId == prior.SiteId && groupId == prior.GroupId && prior.SiteId is not null && await db.Sites.AnyAsync(x => x.Id == prior.SiteId);
            if (!preservesArchivedSite && !await ScopeExists(db, siteId, groupId)) return Results.BadRequest(new { error = "La sede o il gruppo selezionato non è disponibile." });
            var slug = r.Slug.Trim();
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? publishedAt = r.IsPublished ? prior.PublishedAt ?? now : null;
            await using var tx = await db.Database.BeginTransactionAsync();
            int changed;
            try
            {
                changed = await db.KnowledgeArticles.Where(x => x.Id == id && x.Version == r.ExpectedVersion).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Slug, slug).SetProperty(x => x.NormalizedSlug, slug.ToLowerInvariant())
                    .SetProperty(x => x.Title, r.Title.Trim()).SetProperty(x => x.Summary, r.Summary.Trim()).SetProperty(x => x.Body, r.Body.Trim())
                    .SetProperty(x => x.IsPublished, r.IsPublished).SetProperty(x => x.PublishedAt, publishedAt)
                    .SetProperty(x => x.SiteId, siteId).SetProperty(x => x.GroupId, groupId)
                    .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, x => x.Version + 1));
                if (changed == 0) return Results.Conflict(new { error = "L'articolo è stato modificato. Ricaricalo e riprova." });
                var revisionSnapshot = new KnowledgeArticle {
                    Id = id, Slug = slug, NormalizedSlug = slug.ToLowerInvariant(), Title = r.Title.Trim(),
                    Summary = r.Summary.Trim(), Body = r.Body.Trim(), IsPublished = r.IsPublished,
                    SiteId = siteId, GroupId = groupId,
                    Version = r.ExpectedVersion + 1, UpdatedAt = now, PublishedAt = publishedAt
                };
                db.KnowledgeArticleRevisions.Add(Revision(revisionSnapshot, c.User.UserId(), now));
                var action = prior.IsPublished && !r.IsPublished ? "knowledge.article.unpublish" : "knowledge.article.update";
                db.AuditEvents.Add(Audit(c, action, id));
                await db.SaveChangesAsync(); await tx.CommitAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(new { error = "Esiste già un articolo con questo slug." }); }
            catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { error = "Esiste già un articolo con questo slug." }); }
            var article = await db.KnowledgeArticles.AsNoTracking().SingleAsync(x => x.Id == id);
            return Results.Ok(article);
        });
    }

    private static bool Valid(string? slug, string? title, string? summary, string? body) =>
        !string.IsNullOrWhiteSpace(slug) && slug.Length <= 120 && SlugPattern().IsMatch(slug) &&
        !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 160 &&
        summary is not null && summary.Trim().Length <= 500 &&
        !string.IsNullOrWhiteSpace(body) && body.Trim().Length <= 30000;

    private static bool ValidScope(Guid? siteId, Guid? groupId) => siteId is null || groupId is null;

    private static Task<bool> ScopeExists(AppDbContext db, Guid? siteId, Guid? groupId) =>
        siteId is not null ? db.Sites.AnyAsync(x => x.Id == siteId && !x.IsArchived) :
        groupId is not null ? db.DeviceGroups.AnyAsync(x => x.Id == groupId) : Task.FromResult(true);

    private static AuditEvent Audit(HttpContext c, string action, Guid id) =>
        new() { ActorId = c.User.UserId(), Action = action, Target = id.ToString() };

    private static KnowledgeArticleRevision Revision(KnowledgeArticle article, string actorId, DateTimeOffset recordedAt) => new() {
        ArticleId = article.Id, Version = article.Version, Slug = article.Slug, Title = article.Title,
        Summary = article.Summary, Body = article.Body, IsPublished = article.IsPublished,
        SiteId = article.SiteId, GroupId = article.GroupId,
        PublishedAt = article.PublishedAt, RecordedAt = recordedAt, RecordedBy = actorId
    };
}
