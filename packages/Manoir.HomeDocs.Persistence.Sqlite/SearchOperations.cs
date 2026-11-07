using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class SearchOperations(HomeDocsDbContext context) : ISearchOperations
{
    private sealed record Row(string PageId, string Title, string Slug, string CategoryId, string Summary, long Locked, double Rank, string? Excerpt);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchQuery query,
        string contextId,
        bool includeProtectedBody,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var expression = SearchIndex.ToMatchExpression(query);
        if (expression is null || limit <= 0) return [];
        var contextMarker = "|" + contextId + "|";
        var access = includeProtectedBody ? 1 : 0;

        var rows = new List<Row>();
        rows.AddRange(await context.Database.SqlQuery<Row>(
            $"""
            SELECT "PageId", "Title", "Slug", "CategoryId", "Summary", "Locked",
                   bm25("PageSearch", 10.0, 4.0, 1.0, 2.0) AS "Rank",
                   CASE WHEN "Locked" = 0 OR {access} = 1
                        THEN snippet("PageSearch", 2, '', '', '…', 16) ELSE NULL END AS "Excerpt"
            FROM "PageSearch"
            WHERE "PageSearch" MATCH {expression}
              AND ("Contexts" = '' OR instr("Contexts", {contextMarker}) > 0)
              AND ("Locked" = 0 OR {access} = 1)
            ORDER BY "Rank"
            LIMIT {limit}
            """).ToListAsync(cancellationToken));

        if (!includeProtectedBody)
        {
            // Protected pages stay findable by title, summary and category only: their body is never matched.
            var headOnly = "{Title Summary Category} : (" + expression + ")";
            rows.AddRange(await context.Database.SqlQuery<Row>(
                $"""
                SELECT "PageId", "Title", "Slug", "CategoryId", "Summary", "Locked",
                       bm25("PageSearch", 10.0, 4.0, 1.0, 2.0) AS "Rank",
                       NULL AS "Excerpt"
                FROM "PageSearch"
                WHERE "PageSearch" MATCH {headOnly}
                  AND ("Contexts" = '' OR instr("Contexts", {contextMarker}) > 0)
                  AND "Locked" = 1
                ORDER BY "Rank"
                LIMIT {limit}
                """).ToListAsync(cancellationToken));
        }

        return rows.OrderBy(row => row.Rank).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .Select(row => new SearchHit(
                Guid.Parse(row.PageId), row.Title, row.Slug, row.CategoryId, row.Summary,
                string.IsNullOrWhiteSpace(row.Excerpt) ? null : row.Excerpt, row.Locked != 0))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchVocabularyTerm>> GetVocabularyAsync(CancellationToken cancellationToken = default) =>
        await context.Database.SqlQueryRaw<VocabularyRow>(
                "SELECT \"term\" AS \"Term\", \"doc\" AS \"Documents\" FROM \"PageSearchVocab\"")
            .Select(row => new SearchVocabularyTerm(row.Term, row.Documents))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SearchVisiblePage>> ListVisiblePagesAsync(string contextId, CancellationToken cancellationToken = default)
    {
        var contextMarker = "|" + contextId + "|";
        var rows = await context.Database.SqlQuery<VisibleRow>(
            $"""
            SELECT "PageId", "Title" FROM "PageSearch"
            WHERE "Contexts" = '' OR instr("Contexts", {contextMarker}) > 0
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new SearchVisiblePage(Guid.Parse(row.PageId), row.Title)).ToList();
    }

    private sealed record VocabularyRow(string Term, int Documents);
    private sealed record VisibleRow(string PageId, string Title);
}
