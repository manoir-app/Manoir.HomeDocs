namespace Manoir.HomeDocs.Contracts;

public sealed record PublicSearchResult(
    Guid PageId,
    string Title,
    string Slug,
    string Category,
    string Summary,
    string? Excerpt,
    bool RequiresAuthentication,
    IReadOnlyList<string> Path);

public sealed record PublicSearchResponse(
    string Query,
    bool Corrected,
    IReadOnlyList<PublicSearchResult> Results);
