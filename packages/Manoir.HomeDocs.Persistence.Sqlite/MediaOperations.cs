using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class MediaOperations(HomeDocsDbContext context, MediaFileStore files, ILogger<MediaOperations> logger) : IMediaOperations
{
    public async Task<IReadOnlyList<MediaItem>> ListAsync(CancellationToken cancellationToken = default) =>
        (await context.MediaItems.AsNoTracking().OrderBy(media => media.Name).ToListAsync(cancellationToken))
        .Select(ToItem).ToList();

    public async Task<MediaItem?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.MediaItems.AsNoTracking().SingleOrDefaultAsync(media => media.Id == id, cancellationToken);
        return row is null ? null : ToItem(row);
    }

    public async Task CreateAsync(MediaItem item, byte[] content, CancellationToken cancellationToken = default)
    {
        await files.WriteAsync(item.Id, content, cancellationToken);
        try
        {
            context.ChangeTracker.Clear();
            context.MediaItems.Add(new MediaItemEntity
            {
                Id = item.Id,
                Name = item.Name,
                Kind = item.Kind,
                ContentType = item.ContentType,
                Size = item.Size,
                Title = item.Title,
                AltText = item.AltText,
                Folder = item.Folder,
                CreatedAtUnixMilliseconds = item.CreatedAt.ToUnixTimeMilliseconds(),
                Revision = 1
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            files.Delete(item.Id);
            throw;
        }
    }

    public async Task<MediaWriteResult> UpdateAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        var row = await context.MediaItems.SingleOrDefaultAsync(media => media.Id == item.Id, cancellationToken);
        if (row is null) return MediaWriteResult.NotFound;
        if (row.Revision != item.Revision) return MediaWriteResult.VersionConflict;
        row.Name = item.Name;
        row.Title = item.Title;
        row.AltText = item.AltText;
        row.Folder = item.Folder;
        row.Revision = checked(row.Revision + 1);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MediaWriteResult.VersionConflict;
        }
        return MediaWriteResult.Success;
    }

    public async Task<MediaWriteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        var row = await context.MediaItems.SingleOrDefaultAsync(media => media.Id == id, cancellationToken);
        if (row is null) return MediaWriteResult.NotFound;
        if (row.Revision != revision) return MediaWriteResult.VersionConflict;
        context.MediaItems.Remove(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MediaWriteResult.VersionConflict;
        }
        try { files.Delete(id); }
        catch (Exception error) { logger.LogWarning(error, "Media {MediaId} was deleted but its file could not be removed.", id); }
        return MediaWriteResult.Success;
    }

    public async Task<MediaFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken);
        if (item is null) return null;
        try { return new MediaFile(item, files.Open(id)); }
        catch (FileNotFoundException) { return null; }
    }

    private static MediaItem ToItem(MediaItemEntity row) => new(
        row.Id, row.Name, row.Kind, row.ContentType, row.Size, row.Title, row.AltText, row.Folder,
        DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUnixMilliseconds), row.Revision);
}
