using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum HistoryResult
{
    Success,
    NotFound,
    VersionConflict,
    SlugConflict,
    RootInUse,
    InvalidInput
}

public sealed class ContentHistoryLogic(
    IContentHistoryOperations history,
    IContentPageOperations pages,
    IUsageContextOperations contexts)
{
    public async Task<IReadOnlyList<ContentVersionSummary>?> ListAsync(Guid pageId, CancellationToken cancellationToken = default) =>
        await pages.FindAsync(pageId, cancellationToken) is null ? null : await history.ListAsync(pageId, cancellationToken);

    public Task<ContentVersion?> FindAsync(Guid pageId, long revision, CancellationToken cancellationToken = default) =>
        history.FindAsync(pageId, revision, cancellationToken);

    /// <summary>
    /// Restores the title, category, contexts, protection and Markdown of a version as a new revision.
    /// The slug and the publication state stay those of the page, so links and the tree keep working;
    /// contexts that no longer exist are dropped.
    /// </summary>
    public async Task<HistoryResult> RestoreAsync(
        Guid pageId,
        long currentRevision,
        long versionRevision,
        CancellationToken cancellationToken = default)
    {
        var current = await pages.FindAsync(pageId, cancellationToken);
        if (current is null) return HistoryResult.NotFound;
        if (current.Revision != currentRevision) return HistoryResult.VersionConflict;
        var version = await history.FindAsync(pageId, versionRevision, cancellationToken);
        if (version is null) return HistoryResult.NotFound;

        var knownContexts = (await contexts.ListAsync(cancellationToken)).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var restored = current with
        {
            Title = version.Summary.Title,
            Category = version.Category,
            Contexts = version.Contexts.Where(knownContexts.Contains).ToArray(),
            RequiresAuthentication = version.Summary.RequiresAuthentication,
            Markdown = version.Markdown
        };
        if (!ContentPageValidation.IsValid(restored)) return HistoryResult.InvalidInput;

        var outcome = await pages.SaveAsync(restored, false, cancellationToken);
        switch (outcome)
        {
            case PageWriteResult.Success:
                await history.MarkRestoredAsync(pageId, current.Revision + 1, versionRevision, cancellationToken);
                return HistoryResult.Success;
            case PageWriteResult.NotFound: return HistoryResult.NotFound;
            case PageWriteResult.VersionConflict: return HistoryResult.VersionConflict;
            case PageWriteResult.ContextRootInUse: return HistoryResult.RootInUse;
            case PageWriteResult.SlugConflict: return HistoryResult.SlugConflict;
            default: return HistoryResult.InvalidInput;
        }
    }
}
