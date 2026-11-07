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

internal sealed class PageTreeNodeEntity
{
    public Guid Id { get; set; }
    public Guid? ParentNodeId { get; set; }
    public PageTreeNodeEntity? ParentNode { get; set; }
    public List<PageTreeNodeEntity> Children { get; set; } = [];
    public Guid PageId { get; set; }
    public ContentPageEntity Page { get; set; } = null!;
    public bool IsLink { get; set; }
    public int Position { get; set; }
}

internal sealed class MediaItemEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long Size { get; set; }
    public string Title { get; set; } = null!;
    public string AltText { get; set; } = null!;
    public string Folder { get; set; } = null!;
    public long CreatedAtUnixMilliseconds { get; set; }
    public long Revision { get; set; }
}
