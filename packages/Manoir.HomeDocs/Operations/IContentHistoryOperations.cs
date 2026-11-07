using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Operations;

/// <summary>
/// Read side of the page history. Versions are recorded by the page persistence itself, in the same
/// transaction as every page write, so the history cannot drift from the pages.
/// </summary>
public interface IContentHistoryOperations
{
    /// <summary>Versions of a page, newest first.</summary>
    Task<IReadOnlyList<ContentVersionSummary>> ListAsync(Guid pageId, CancellationToken cancellationToken = default);

    Task<ContentVersion?> FindAsync(Guid pageId, long revision, CancellationToken cancellationToken = default);

    /// <summary>Labels the version created by a restore, so the history shows where it came from.</summary>
    Task MarkRestoredAsync(Guid pageId, long revision, long restoredFromRevision, CancellationToken cancellationToken = default);
}
