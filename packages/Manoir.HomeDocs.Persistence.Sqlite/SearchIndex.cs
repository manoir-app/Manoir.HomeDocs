using System.Text;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Manoir.HomeDocs.Persistence.Sqlite;

/// <summary>Writes the FTS5 table. Statements join the ambient EF transaction of the context.</summary>
internal static class SearchIndex
{
    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.Ordinal)
    {
        ["page"] = "page",
        ["piece"] = "pièce",
        ["emplacement"] = "emplacement rangement",
        ["procedure"] = "procédure",
        ["equipement"] = "équipement appareil",
        ["depannage"] = "dépannage problème",
        ["technique"] = "documentation technique"
    };

    public static async Task WriteAsync(HomeDocsDbContext context, ContentPage page, CancellationToken cancellationToken)
    {
        var id = page.Id.ToString();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PageSearch\" WHERE \"PageId\" = {id}", cancellationToken);
        if (!page.IsPublished) return;
        var summary = SearchText.BuildSummary(page.Markdown);
        var body = SearchText.ToPlainText(page.Markdown);
        var categoryLabel = CategoryLabels.GetValueOrDefault(page.Category, page.Category);
        var contexts = page.Contexts.Count == 0 ? string.Empty : "|" + string.Join('|', page.Contexts) + "|";
        var locked = page.RequiresAuthentication ? 1 : 0;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "PageSearch" ("Title", "Summary", "Body", "Category", "PageId", "Slug", "CategoryId", "Contexts", "Locked")
            VALUES ({page.Title}, {summary}, {body}, {categoryLabel}, {id}, {page.Slug}, {page.Category}, {contexts}, {locked})
            """,
            cancellationToken);
    }

    public static async Task RemoveAsync(HomeDocsDbContext context, Guid pageId, CancellationToken cancellationToken)
    {
        var id = pageId.ToString();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PageSearch\" WHERE \"PageId\" = {id}", cancellationToken);
    }

    /// <summary>Rebuilds the whole index from the stored pages. Failures are logged, never fatal.</summary>
    public static async Task RebuildAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Manoir.HomeDocs.Search");
        try
        {
            var pages = await scope.ServiceProvider.GetRequiredService<IContentPageOperations>().ListAsync(cancellationToken);
            var context = scope.ServiceProvider.GetRequiredService<HomeDocsDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM \"PageSearch\"", cancellationToken);
            foreach (var page in pages) await WriteAsync(context, page, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "The search index could not be rebuilt; search results may be incomplete.");
        }
    }

    /// <summary>Builds an FTS5 MATCH expression from engine-neutral groups. Only quoted tokens are emitted.</summary>
    public static string? ToMatchExpression(SearchQuery query)
    {
        var groups = new List<string>();
        foreach (var group in query.Groups)
        {
            var alternatives = group
                .Select(term => (Text: SearchText.Normalize(term.Text), term.Prefix))
                .Where(term => term.Text.Length > 0)
                .Select(term => "\"" + term.Text + "\"" + (term.Prefix ? "*" : string.Empty))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (alternatives.Length > 0) groups.Add("(" + string.Join(" OR ", alternatives) + ")");
        }
        return groups.Count == 0 ? null : string.Join(" AND ", groups);
    }
}
