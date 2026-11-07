using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Operations;

public enum MediaWriteResult
{
    Success,
    NotFound,
    VersionConflict
}

public sealed record MediaFile(MediaItem Item, Stream Content);

public interface IMediaOperations
{
    Task<IReadOnlyList<MediaItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaItem?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task CreateAsync(MediaItem item, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Updates name, title, alternative text and folder when the revision matches.</summary>
    Task<MediaWriteResult> UpdateAsync(MediaItem item, CancellationToken cancellationToken = default);

    Task<MediaWriteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored bytes; the caller disposes the stream.</summary>
    Task<MediaFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default);
}
