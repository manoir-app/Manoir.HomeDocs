using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum ContentPageResult
{
    Success,
    InvalidInput,
    NotFound,
    SlugConflict,
    VersionConflict,
    InvalidParent,
    HasChildren,
    RootInUse
}

public sealed class ContentPageLogic(IContentPageOperations pages)
{
    public Task<IReadOnlyList<ContentPage>> ListAsync(CancellationToken cancellationToken = default) =>
        pages.ListAsync(cancellationToken);

    public Task<ContentPage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        pages.FindAsync(id, cancellationToken);

    public async Task<(ContentPageResult Result, Guid Id)> CreateAsync(
        ContentPageInput input,
        Guid? parentNodeId = null,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        if (!TryCreatePage(id, input, false, 1, out var page))
        {
            return (ContentPageResult.InvalidInput, Guid.Empty);
        }

        return (ToResult(await pages.SaveAsync(page!, true, cancellationToken, parentNodeId)), id);
    }

    public async Task<ContentPageResult> UpdateAsync(Guid id, long revision, ContentPageInput input, CancellationToken cancellationToken = default)
    {
        var existing = await pages.FindAsync(id, cancellationToken);
        if (existing is null) return ContentPageResult.NotFound;
        if (existing.Revision != revision) return ContentPageResult.VersionConflict;
        if (!TryCreatePage(id, input, existing.IsPublished, revision, out var page)) return ContentPageResult.InvalidInput;
        return ToResult(await pages.SaveAsync(page!, false, cancellationToken));
    }

    public async Task<ContentPageResult> SetPublicationAsync(Guid id, long revision, bool published, CancellationToken cancellationToken = default)
    {
        var page = await pages.FindAsync(id, cancellationToken);
        if (page is null) return ContentPageResult.NotFound;
        if (page.Revision != revision) return ContentPageResult.VersionConflict;
        return ToResult(await pages.SaveAsync(page with { IsPublished = published }, false, cancellationToken));
    }

    public async Task<(ContentPageResult Result, Guid Id)> DuplicateAsync(
        Guid sourceId,
        string? title,
        string? slug,
        Guid? parentNodeId = null,
        CancellationToken cancellationToken = default)
    {
        var source = await pages.FindAsync(sourceId, cancellationToken);
        if (source is null) return (ContentPageResult.NotFound, Guid.Empty);
        return await CreateAsync(
            new ContentPageInput(title, slug, source.Category, source.Contexts, source.RequiresAuthentication, source.Markdown),
            parentNodeId,
            cancellationToken);
    }

    public async Task<ContentPageResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken = default) =>
        ToResult(await pages.DeleteAsync(id, revision, cancellationToken));

    private static bool TryCreatePage(Guid id, ContentPageInput input, bool isPublished, long revision, out ContentPage? page)
    {
        page = null;
        var title = input.Title?.Trim();
        var slug = input.Slug;
        var category = input.Category?.Trim();
        var contexts = input.Contexts;
        if (title is null || slug is null || category is null || contexts is null ||
            input.RequiresAuthentication is null || input.Markdown is null)
        {
            return false;
        }

        page = new ContentPage(id, title, slug, category, contexts.ToArray(), input.RequiresAuthentication.Value, isPublished, input.Markdown, revision);
        if (!ContentPageValidation.IsValid(page))
        {
            page = null;
            return false;
        }
        return true;
    }

    private static ContentPageResult ToResult(PageWriteResult result) => result switch
    {
        PageWriteResult.Success => ContentPageResult.Success,
        PageWriteResult.NotFound => ContentPageResult.NotFound,
        PageWriteResult.SlugConflict => ContentPageResult.SlugConflict,
        PageWriteResult.VersionConflict => ContentPageResult.VersionConflict,
        PageWriteResult.InvalidParent => ContentPageResult.InvalidParent,
        PageWriteResult.HasChildren => ContentPageResult.HasChildren,
        PageWriteResult.InvalidContext => ContentPageResult.InvalidInput,
        PageWriteResult.ContextRootInUse => ContentPageResult.RootInUse,
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
