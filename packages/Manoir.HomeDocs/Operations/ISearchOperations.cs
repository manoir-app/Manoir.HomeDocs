namespace Manoir.HomeDocs.Operations;

/// <summary>One alternative of a search group. A text with spaces is an exact phrase.</summary>
public sealed record SearchTerm(string Text, bool Prefix);

/// <summary>
/// A search request: every group must match (AND); a group matches when any of its terms match (OR).
/// Texts are already normalized (lower case, no diacritics).
/// </summary>
public sealed record SearchQuery(IReadOnlyList<IReadOnlyList<SearchTerm>> Groups);

public sealed record SearchHit(
    Guid PageId,
    string Title,
    string Slug,
    string Category,
    string Summary,
    string? Excerpt,
    bool RequiresAuthentication);

public sealed record SearchVisiblePage(Guid PageId, string Title);

public sealed record SearchVocabularyTerm(string Term, int Documents);

/// <summary>Read side of the local search index. The index only holds published pages.</summary>
public interface ISearchOperations
{
    /// <summary>
    /// Returns the best hits available in the context, best first. The body of a protected page is
    /// neither matched nor excerpted unless <paramref name="includeProtectedBody"/> is true.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchQuery query,
        string contextId,
        bool includeProtectedBody,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchVocabularyTerm>> GetVocabularyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchVisiblePage>> ListVisiblePagesAsync(string contextId, CancellationToken cancellationToken = default);
}
