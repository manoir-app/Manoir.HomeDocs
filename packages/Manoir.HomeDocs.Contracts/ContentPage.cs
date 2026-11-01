namespace Manoir.HomeDocs.Contracts;

public sealed record ContentPage(
    Guid Id,
    string Title,
    string Slug,
    string Category,
    IReadOnlyList<string> Contexts,
    bool RequiresAuthentication,
    bool IsPublished,
    string Markdown,
    long Revision);

public sealed record ContentPageInput(
    string? Title,
    string? Slug,
    string? Category,
    IReadOnlyList<string>? Contexts,
    bool? RequiresAuthentication,
    string? Markdown);
