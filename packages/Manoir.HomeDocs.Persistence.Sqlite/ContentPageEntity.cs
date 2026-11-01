namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class ContentPageEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string ContextsJson { get; set; } = null!;
    public bool RequiresAuthentication { get; set; }
    public bool IsPublished { get; set; }
    public string FileId { get; set; } = null!;
    public long Revision { get; set; }
}

internal sealed class ContentPageRedirectEntity
{
    public string Slug { get; set; } = null!;
    public Guid PageId { get; set; }
    public ContentPageEntity Page { get; set; } = null!;
}
