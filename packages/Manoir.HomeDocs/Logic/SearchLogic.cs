using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public sealed class SearchLogic(
    UsageContextLogic contexts,
    ISearchOperations search,
    IPageTreeOperations tree)
{
    public const int MaxQueryLength = 100;
    public const int MaxTerms = 8;
    public const int DefaultLimit = 20;

    /// <summary>
    /// Common household synonyms. Each line is a group of interchangeable words or phrases; they are
    /// matched after normalization (lower case, no accents, hyphens as spaces).
    /// </summary>
    private static readonly string[][] SynonymGroups =
    [
        ["sopalin", "essuie-tout", "papier absorbant"],
        ["pq", "papier toilette", "papier wc", "papier cul"],
        ["wifi", "wi-fi", "internet", "reseau"],
        ["plaque", "plaque de cuisson", "table de cuisson", "plaques"],
        ["courant", "electricite", "electrique", "disjoncteur", "panne de courant"],
        ["poubelle", "dechets", "ordures", "tri"],
        ["frigo", "refrigerateur", "congelateur"],
        ["lave vaisselle", "lave-vaisselle", "lv"],
        ["lave linge", "lave-linge", "machine a laver"],
        ["tele", "television", "tv"],
        ["chauffage", "radiateur", "thermostat"]
    ];

    private static readonly IReadOnlyList<string[]> NormalizedSynonyms = SynonymGroups
        .Select(group => group.Select(SearchText.Normalize).Distinct(StringComparer.Ordinal).ToArray())
        .ToArray();

    /// <summary>Returns null when the invocation (context, profile, device) is unknown.</summary>
    public async Task<PublicSearchResponse?> SearchAsync(
        string? contextId,
        string? profile,
        string? device,
        string? text,
        bool hasProtectedAccess,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!await contexts.IsKnownInvocationAsync(contextId, profile, device, cancellationToken)) return null;
        limit = Math.Clamp(limit, 1, 50);
        var rawText = (text ?? string.Empty).Length > MaxQueryLength ? text![..MaxQueryLength] : text ?? string.Empty;
        var terms = SearchText.Tokenize(rawText).Take(MaxTerms).ToArray();
        if (terms.Length == 0) return new PublicSearchResponse(rawText.Trim(), false, []);

        var endsWithSpace = char.IsWhiteSpace(rawText[^1]);
        var groups = terms.Select((term, index) => BuildGroup(term, index == terms.Length - 1 && !endsWithSpace)).ToList();
        var hits = await search.SearchAsync(new SearchQuery(groups), contextId!, hasProtectedAccess, limit, cancellationToken);
        var corrected = false;

        if (hits.Count == 0)
        {
            var vocabulary = await search.GetVocabularyAsync(cancellationToken);
            var correctedGroups = CorrectGroups(terms, endsWithSpace, vocabulary);
            if (correctedGroups is not null)
            {
                hits = await search.SearchAsync(new SearchQuery(correctedGroups), contextId!, hasProtectedAccess, limit, cancellationToken);
                corrected = hits.Count > 0;
            }
        }

        var results = await AttachPathsAsync(hits, contextId!, cancellationToken);
        return new PublicSearchResponse(rawText.Trim(), corrected, results);
    }

    private static List<SearchTerm> BuildGroup(string term, bool prefix)
    {
        var group = new List<SearchTerm> { new(term, prefix && term.Length >= 2) };
        foreach (var synonyms in NormalizedSynonyms)
        {
            var matches = synonyms.Contains(term, StringComparer.Ordinal) ||
                (prefix && term.Length >= 3 && synonyms.Any(synonym => !synonym.Contains(' ') && synonym.StartsWith(term, StringComparison.Ordinal)));
            if (!matches) continue;
            foreach (var synonym in synonyms) group.Add(new SearchTerm(synonym, false));
        }
        return group;
    }

    /// <summary>
    /// Replaces each word that is not in the index by the closest indexed words (typos and phonetic
    /// spellings). Returns null when nothing could be corrected.
    /// </summary>
    private static List<List<SearchTerm>>? CorrectGroups(
        string[] terms,
        bool endsWithSpace,
        IReadOnlyList<SearchVocabularyTerm> vocabulary)
    {
        if (vocabulary.Count == 0) return null;
        var known = vocabulary.ToDictionary(item => item.Term, item => item.Documents, StringComparer.Ordinal);
        var folded = vocabulary.Select(item => (item.Term, item.Documents, Folded: SearchText.Fold(item.Term))).ToArray();
        var changed = false;
        var groups = new List<List<SearchTerm>>(terms.Length);
        for (var index = 0; index < terms.Length; index++)
        {
            var term = terms[index];
            var prefix = index == terms.Length - 1 && !endsWithSpace;
            var inIndex = known.ContainsKey(term) ||
                (prefix && vocabulary.Any(item => item.Term.StartsWith(term, StringComparison.Ordinal))) ||
                BuildGroup(term, prefix).Count > 1;
            if (inIndex || term.Length < 3 || term.All(char.IsDigit))
            {
                groups.Add(BuildGroup(term, prefix));
                continue;
            }

            var foldedTerm = SearchText.Fold(term);
            var allowed = foldedTerm.Length >= 8 ? 2 : foldedTerm.Length >= 4 ? 1 : 0;
            var candidates = folded
                .Select(item => (item.Term, item.Documents, Distance: Closest(foldedTerm, item.Folded, prefix)))
                .Where(item => item.Distance <= allowed)
                .OrderBy(item => item.Distance).ThenByDescending(item => item.Documents)
                .Take(5)
                .ToArray();
            if (candidates.Length == 0)
            {
                groups.Add(BuildGroup(term, prefix));
                continue;
            }
            changed = true;
            var group = new List<SearchTerm>();
            foreach (var candidate in candidates)
                group.AddRange(BuildGroup(candidate.Term, false));
            groups.Add(group);
        }
        return changed ? groups : null;
    }

    private static int Closest(string foldedTerm, string foldedWord, bool prefix)
    {
        var distance = SearchText.Distance(foldedTerm, foldedWord);
        if (prefix && foldedWord.Length > foldedTerm.Length)
            distance = Math.Min(distance, SearchText.Distance(foldedTerm, foldedWord[..foldedTerm.Length]));
        return distance;
    }

    private async Task<IReadOnlyList<PublicSearchResult>> AttachPathsAsync(
        IReadOnlyList<SearchHit> hits,
        string contextId,
        CancellationToken cancellationToken)
    {
        if (hits.Count == 0) return [];
        var nodes = (await tree.ListAsync(cancellationToken)).ToDictionary(node => node.Id);
        var visible = (await search.ListVisiblePagesAsync(contextId, cancellationToken)).ToDictionary(page => page.PageId);
        var ownNodes = nodes.Values.Where(node => !node.IsLink).GroupBy(node => node.PageId).ToDictionary(group => group.Key, group => group.First());

        return hits.Select(hit => new PublicSearchResult(
            hit.PageId, hit.Title, hit.Slug, hit.Category, hit.Summary, hit.Excerpt,
            hit.RequiresAuthentication, PathOf(hit.PageId))).ToList();

        IReadOnlyList<string> PathOf(Guid pageId)
        {
            var path = new List<string>();
            if (!ownNodes.TryGetValue(pageId, out var node)) return path;
            var seen = new HashSet<Guid> { node.Id };
            while (node.ParentNodeId is Guid parentId && nodes.TryGetValue(parentId, out var parent) && seen.Add(parent.Id))
            {
                // Only titles that the navigation of this context would show.
                if (visible.TryGetValue(parent.PageId, out var page)) path.Add(page.Title);
                node = parent;
            }
            path.Reverse();
            return path;
        }
    }
}
