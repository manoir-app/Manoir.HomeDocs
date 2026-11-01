using System.Text;
using System.Text.RegularExpressions;
using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Logic;

public static partial class ContentPageValidation
{
    private static readonly HashSet<string> Categories =
        ["page", "piece", "emplacement", "procedure", "equipement", "depannage", "technique"];

    public static bool IsValid(ContentPage page) =>
        page.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(page.Title) && page.Title == page.Title.Trim() &&
        page.Title.Length <= 200 && !page.Title.Any(char.IsControl) &&
        IsSlug(page.Slug) && page.Category is not null && Categories.Contains(page.Category) &&
        page.Contexts is not null && page.Contexts.Count <= 100 &&
        page.Contexts.All(IsSlug) &&
        page.Contexts.Distinct(StringComparer.Ordinal).Count() == page.Contexts.Count &&
        page.Markdown is not null && Encoding.UTF8.GetByteCount(page.Markdown) <= 1024 * 1024 &&
        page.Revision > 0;

    public static bool IsSlug(string? value) =>
        value is { Length: > 0 and <= 80 } && SlugPattern().IsMatch(value);

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
