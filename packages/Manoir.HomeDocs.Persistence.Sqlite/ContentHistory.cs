using System.Text;
using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Manoir.HomeDocs.Persistence.Sqlite;

/// <summary>Records page versions. Writes join the ambient EF transaction of the page write.</summary>
internal static class ContentHistory
{
    public const int MaxVersionsPerPage = 200;

    public static string EventFor(bool isNew, bool wasPublished, bool isPublished) =>
        isNew ? "created" : wasPublished == isPublished ? "edited" : isPublished ? "published" : "unpublished";

    public static async Task RecordAsync(HomeDocsDbContext context, ContentPage page, string eventName, CancellationToken cancellationToken)
    {
        context.ContentPageVersions.Add(new ContentPageVersionEntity
        {
            PageId = page.Id,
            Revision = page.Revision,
            Event = eventName,
            Title = page.Title,
            Slug = page.Slug,
            Category = page.Category,
            ContextsJson = JsonSerializer.Serialize(page.Contexts),
            RequiresAuthentication = page.RequiresAuthentication,
            IsPublished = page.IsPublished,
            Markdown = page.Markdown,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        await context.SaveChangesAsync(cancellationToken);
        var oldest = page.Revision - MaxVersionsPerPage;
        if (oldest > 0)
        {
            await context.ContentPageVersions
                .Where(version => version.PageId == page.Id && version.Revision <= oldest)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>Pages created before the history existed get one baseline version of their current state.</summary>
    public static async Task EnsureBaselineAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Manoir.HomeDocs.History");
        try
        {
            var pages = await scope.ServiceProvider.GetRequiredService<IContentPageOperations>().ListAsync(cancellationToken);
            var context = scope.ServiceProvider.GetRequiredService<HomeDocsDbContext>();
            var recorded = (await context.ContentPageVersions.AsNoTracking()
                .Select(version => new { version.PageId, version.Revision }).ToListAsync(cancellationToken))
                .Select(version => (version.PageId, version.Revision)).ToHashSet();
            foreach (var page in pages.Where(page => !recorded.Contains((page.Id, page.Revision))))
                await RecordAsync(context, page, "baseline", cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "The page history baseline could not be created.");
        }
    }
}

internal sealed class ContentHistoryOperations(HomeDocsDbContext context) : IContentHistoryOperations
{
    public async Task<IReadOnlyList<ContentVersionSummary>> ListAsync(Guid pageId, CancellationToken cancellationToken = default) =>
        (await context.ContentPageVersions.AsNoTracking()
            .Where(version => version.PageId == pageId)
            .OrderByDescending(version => version.Revision)
            .ToListAsync(cancellationToken))
        .Select(ToSummary).ToList();

    public async Task<ContentVersion?> FindAsync(Guid pageId, long revision, CancellationToken cancellationToken = default)
    {
        var row = await context.ContentPageVersions.AsNoTracking()
            .SingleOrDefaultAsync(version => version.PageId == pageId && version.Revision == revision, cancellationToken);
        if (row is null) return null;
        var contexts = JsonSerializer.Deserialize<string[]>(row.ContextsJson)
            ?? throw new InvalidDataException("Invalid stored contexts.");
        return new ContentVersion(ToSummary(row), row.Category, contexts, row.Markdown);
    }

    public async Task MarkRestoredAsync(Guid pageId, long revision, long restoredFromRevision, CancellationToken cancellationToken = default)
    {
        await context.ContentPageVersions
            .Where(version => version.PageId == pageId && version.Revision == revision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(version => version.Event, "restored")
                .SetProperty(version => version.RestoredFromRevision, restoredFromRevision), cancellationToken);
    }

    private static ContentVersionSummary ToSummary(ContentPageVersionEntity row) => new(
        row.Revision, row.Event, row.Title, row.Slug, row.IsPublished, row.RequiresAuthentication,
        DateTimeOffset.FromUnixTimeMilliseconds(row.SavedAtUnixMilliseconds),
        Encoding.UTF8.GetByteCount(row.Markdown), row.RestoredFromRevision);
}
