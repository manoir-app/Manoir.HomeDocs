namespace Manoir.HomeDocs.Contracts;

/// <summary>One saved state of a page. Events: created, edited, published, unpublished, restored, baseline.</summary>
public sealed record ContentVersionSummary(
    long Revision,
    string Event,
    string Title,
    string Slug,
    bool IsPublished,
    bool RequiresAuthentication,
    DateTimeOffset SavedAt,
    int Size,
    long? RestoredFromRevision);

public sealed record ContentVersion(
    ContentVersionSummary Summary,
    string Category,
    IReadOnlyList<string> Contexts,
    string Markdown);

/// <summary>A link found in a page. Kind: page, media or external. Status: ok, redirected, draft, broken or unchecked.</summary>
public sealed record PageLink(string Href, string Kind, string Status, Guid? TargetId, string? TargetTitle);

public sealed record IncomingLink(Guid PageId, string Title, string Slug, bool IsPublished);

public sealed record PageLinkReport(IReadOnlyList<PageLink> Outgoing, IReadOnlyList<IncomingLink> Incoming);

public sealed record BrokenLink(Guid PageId, string PageTitle, string PageSlug, PageLink Link);
