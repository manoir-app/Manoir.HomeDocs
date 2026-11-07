using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Manoir.HomeDocs.Logic;

/// <summary>Text helpers shared by the search index and the search logic.</summary>
public static partial class SearchText
{
    public const int SummaryLength = 240;

    /// <summary>Lower case, no diacritics, only letters and digits separated by single spaces.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                var lower = char.ToLowerInvariant(character);
                builder.Append(lower switch { 'œ' => 'o', 'æ' => 'a', 'ß' => 's', _ => lower });
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString();
    }

    public static IReadOnlyList<string> Tokenize(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? [] : normalized.Split(' ');
    }

    /// <summary>Plain text of a Markdown document: no markup, links reduced to their label.</summary>
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        var text = FencePattern().Replace(markdown, " ");
        text = ImagePattern().Replace(text, "$1");
        text = LinkPattern().Replace(text, "$1");
        text = HtmlPattern().Replace(text, " ");
        text = LineMarkerPattern().Replace(text, string.Empty);
        text = text.Replace("`", string.Empty).Replace("*", string.Empty).Replace("~~", string.Empty);
        return WhitespacePattern().Replace(text, " ").Trim();
    }

    /// <summary>First paragraph that is not a heading, as plain text, shortened.</summary>
    public static string BuildSummary(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;
        foreach (var block in BlankLinePattern().Split(FencePattern().Replace(markdown, "\n\n")))
        {
            var trimmed = block.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var plain = ToPlainText(block);
            if (plain.Length == 0) continue;
            return plain.Length <= SummaryLength ? plain : plain[..(SummaryLength - 1)].TrimEnd() + "…";
        }
        return string.Empty;
    }

    /// <summary>
    /// Rough French phonetic folding used only to compare a mistyped word with indexed words.
    /// Input must already be normalized.
    /// </summary>
    public static string Fold(string word)
    {
        var folded = word.Replace("eau", "o").Replace("au", "o").Replace("ph", "f")
            .Replace("qu", "k").Replace("ai", "e").Replace("ei", "e").Replace("y", "i")
            .Replace("h", string.Empty);
        var builder = new StringBuilder(folded.Length);
        for (var index = 0; index < folded.Length; index++)
        {
            var character = folded[index];
            if (character == 'c') character = index + 1 < folded.Length && folded[index + 1] is 'e' or 'i' ? 's' : 'k';
            if (character == 'q') character = 'k';
            if (builder.Length > 0 && builder[^1] == character) continue;
            builder.Append(character);
        }
        while (builder.Length > 3 && builder[^1] is 's' or 'x' or 't' or 'd' or 'z') builder.Length--;
        return builder.ToString();
    }

    /// <summary>Optimal string alignment distance (Levenshtein plus adjacent transpositions).</summary>
    public static int Distance(string left, string right)
    {
        var previousPrevious = new int[right.Length + 1];
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++) previous[column] = column;
        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1), previous[column - 1] + cost);
                if (row > 1 && column > 1 && left[row - 1] == right[column - 2] && left[row - 2] == right[column - 1])
                    value = Math.Min(value, previousPrevious[column - 2] + 1);
                current[column] = value;
            }
            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }
        return previous[right.Length];
    }

    [GeneratedRegex("```.*?```|~~~.*?~~~", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlPattern();

    [GeneratedRegex(@"^\s{0,3}(?:#{1,6}\s+|>\s?|[-+]\s+|\d+[.)]\s+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LineMarkerPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\r?\n\s*\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLinePattern();
}
