using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Operations;

public enum PageWriteResult
{
    Success,
    NotFound,
    SlugConflict,
    VersionConflict
}

public interface IContentPageOperations
{
    Task<IReadOnlyList<ContentPage>> ListAsync(CancellationToken cancellationToken = default);
    Task<ContentPage?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PageWriteResult> SaveAsync(ContentPage page, bool isNew, CancellationToken cancellationToken = default);
    Task<PageWriteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken = default);
    Task<Guid?> ResolvePublishedSlugAsync(string slug, CancellationToken cancellationToken = default);
}
