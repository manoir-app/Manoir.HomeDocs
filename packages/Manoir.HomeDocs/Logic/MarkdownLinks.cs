using System.Text.RegularExpressions;

namespace Manoir.HomeDocs.Logic;

public enum LinkTargetKind
{
    Page,
    Media,
    External
}

public sealed record MarkdownLink(string Href, LinkTargetKind Kind, string? Slug, Guid? MediaId);

/// <summary>Finds the links and images of a Markdown document that point inside the manual or outside it.</summary>
public static partial class MarkdownLinks
{
    public static IReadOnlyList<MarkdownLink> Extract(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return [];
        var text = InlineCodePattern().Replace(FencePattern().Replace(markdown, " "), " ");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MarkdownLink>();
        foreach (Match match in TargetPattern().Matches(text))
        {
            var href = match.Groups[1].Value.Trim();
            if (href.Length == 0 || href.StartsWith('#') || !seen.Add(href)) continue;
            var link = Classify(href);
            if (link is not null) result.Add(link);
        }
        return result;
    }

    private static MarkdownLink? Classify(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal) || SchemePattern().IsMatch(href))
            return new MarkdownLink(href, LinkTargetKind.External, null, null);

        var path = href;
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];
        var mediaMatch = MediaPattern().Match(path);
        if (mediaMatch.Success && Guid.TryParse(mediaMatch.Groups[1].Value, out var mediaId))
            return new MarkdownLink(href, LinkTargetKind.Media, null, mediaId);

        string segment;
        try
        {
            segment = Uri.UnescapeDataString(path.Trim('/'));
        }
        catch (UriFormatException)
        {
            return null;
        }
        return ContentPageValidation.IsSlug(segment) && !path.Trim('/').Contains('/')
            ? new MarkdownLink(href, LinkTargetKind.Page, segment, null)
            : null;
    }

    [GeneratedRegex("```.*?```|~~~.*?~~~", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FencePattern();

    [GeneratedRegex("`[^`\n]*`", RegexOptions.CultureInvariant)]
    private static partial Regex InlineCodePattern();

    [GeneratedRegex(@"\]\(\s*<?([^)\s>]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TargetPattern();

    [GeneratedRegex(@"\A[a-zA-Z][a-zA-Z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();

    [GeneratedRegex(
        @"\A/api/public/media/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex MediaPattern();
}
