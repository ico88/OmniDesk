namespace OmniDesk.Api.Data;

public sealed class KnowledgeArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = "";
    public string NormalizedSlug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsPublished { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? GroupId { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class KnowledgeArticleRevision
{
    public Guid ArticleId { get; set; }
    public long Version { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsPublished { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? GroupId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? RecordedBy { get; set; }
}
