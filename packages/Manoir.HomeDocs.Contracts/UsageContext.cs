namespace Manoir.HomeDocs.Contracts;

public sealed record UsageContext(string Id, string Name, Guid? RootNodeId);

public sealed record PublicContentPage(
    Guid Id,
    string Title,
    string Slug,
    string Category,
    IReadOnlyList<string> Contexts,
    string Markdown,
    bool RequiresAuthentication);

public sealed record PublicNavigationItem(
    Guid NodeId,
    Guid PageId,
    string Title,
    string Slug,
    bool IsLink,
    bool RequiresAuthentication,
    IReadOnlyList<PublicNavigationItem> Children);

public sealed record PublicNavigation(
    string ContextId,
    string ContextName,
    string Profile,
    string? Device,
    PublicNavigationItem Root,
    IReadOnlyList<PublicNavigationItem> Children);
