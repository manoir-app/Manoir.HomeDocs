namespace Manoir.HomeDocs.Contracts;

/// <summary>A stored image or PDF of the local media library.</summary>
public sealed record MediaItem(
    Guid Id,
    string Name,
    string Kind,
    string ContentType,
    long Size,
    string Title,
    string AltText,
    string Folder,
    DateTimeOffset CreatedAt,
    long Revision);

public sealed record MediaUsage(Guid PageId, string Title, string Slug, bool IsPublished);

/// <summary>A media item as shown by the back-office, with its public URL and ready-to-insert Markdown.</summary>
public sealed record AdminMediaItem(
    Guid Id,
    string Name,
    string Kind,
    string ContentType,
    long Size,
    string Title,
    string AltText,
    string Folder,
    DateTimeOffset CreatedAt,
    long Revision,
    string Url,
    string Markdown,
    int UsageCount);

public sealed record AdminMediaDetail(AdminMediaItem Media, IReadOnlyList<MediaUsage> Usages);
