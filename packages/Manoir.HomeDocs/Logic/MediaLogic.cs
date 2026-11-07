using System.Text;
using System.Text.RegularExpressions;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum MediaOutcome
{
    Success,
    InvalidInput,
    UnsupportedType,
    TooLarge,
    NotFound,
    Conflict,
    InUse
}

public sealed record MediaResult(MediaOutcome Outcome, AdminMediaDetail? Detail = null, IReadOnlyList<MediaUsage>? Usages = null);

public enum PublicMediaResult
{
    Found,
    RequiresPin,
    NotFound
}

public sealed record PublicMediaResolution(PublicMediaResult Result, MediaFile? File, bool IsProtected);

public sealed partial class MediaLogic(
    IMediaOperations media,
    IContentPageOperations pages,
    UsageContextLogic contexts)
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const string PublicPath = "/api/public/media/";

    /// <summary>Recognizes the supported formats from their first bytes, never from the file name.</summary>
    public static (string Kind, string ContentType)? Sniff(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.StartsWith(png)) return ("image", "image/png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ("image", "image/jpeg");
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return ("image", "image/gif");
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ("image", "image/webp");
        if (bytes.StartsWith("%PDF-"u8)) return ("pdf", "application/pdf");
        return null;
    }

    public async Task<IReadOnlyList<AdminMediaItem>> ListAsync(
        string? text,
        string? folder,
        string? kind,
        CancellationToken cancellationToken = default)
    {
        var items = await media.ListAsync(cancellationToken);
        var counts = (await ScanUsagesAsync(cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        var terms = SearchText.Tokenize(text);
        var wantedFolder = SearchText.Normalize(folder);
        return items
            .Where(item => string.IsNullOrEmpty(kind) || item.Kind == kind)
            .Where(item => wantedFolder.Length == 0 || SearchText.Normalize(item.Folder) == wantedFolder)
            .Where(item =>
            {
                if (terms.Count == 0) return true;
                var haystack = SearchText.Normalize($"{item.Name} {item.Title} {item.AltText} {item.Folder}");
                return terms.All(term => haystack.Contains(term, StringComparison.Ordinal));
            })
            .OrderBy(item => item.Folder, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => ToAdmin(item, counts.GetValueOrDefault(item.Id)))
            .ToList();
    }

    public async Task<AdminMediaDetail?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await media.FindAsync(id, cancellationToken);
        return item is null ? null : await DetailAsync(item, cancellationToken);
    }

    public async Task<MediaResult> UploadAsync(
        string? fileName,
        byte[] content,
        string? title,
        string? altText,
        string? folder,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0) return new(MediaOutcome.InvalidInput);
        if (content.Length > MaxBytes) return new(MediaOutcome.TooLarge);
        if (Sniff(content) is not { } format) return new(MediaOutcome.UnsupportedType);
        var name = CleanName(fileName, format.ContentType);
        var fields = CleanFields(title ?? Path.GetFileNameWithoutExtension(name), altText, folder);
        if (name is null || fields is null) return new(MediaOutcome.InvalidInput);

        var item = new MediaItem(Guid.NewGuid(), name, format.Kind, format.ContentType, content.Length,
            fields.Value.Title, fields.Value.AltText, fields.Value.Folder, DateTimeOffset.UtcNow, 1);
        await media.CreateAsync(item, content, cancellationToken);
        return new(MediaOutcome.Success, new AdminMediaDetail(ToAdmin(item, 0), []));
    }

    public async Task<MediaResult> UpdateAsync(
        Guid id,
        long revision,
        string? name,
        string? title,
        string? altText,
        string? folder,
        CancellationToken cancellationToken = default)
    {
        var current = await media.FindAsync(id, cancellationToken);
        if (current is null) return new(MediaOutcome.NotFound);
        var cleanName = CleanName(name ?? current.Name, current.ContentType);
        var fields = CleanFields(title ?? current.Title, altText ?? current.AltText, folder ?? current.Folder);
        if (cleanName is null || fields is null) return new(MediaOutcome.InvalidInput);

        var updated = current with
        {
            Name = cleanName,
            Title = fields.Value.Title,
            AltText = fields.Value.AltText,
            Folder = fields.Value.Folder,
            Revision = revision
        };
        switch (await media.UpdateAsync(updated, cancellationToken))
        {
            case MediaWriteResult.NotFound: return new(MediaOutcome.NotFound);
            case MediaWriteResult.VersionConflict: return new(MediaOutcome.Conflict);
        }
        var stored = await media.FindAsync(id, cancellationToken);
        return stored is null ? new(MediaOutcome.NotFound) : new(MediaOutcome.Success, await DetailAsync(stored, cancellationToken));
    }

    /// <summary>A used media is only deleted when <paramref name="force"/> is true; otherwise its usages are returned.</summary>
    public async Task<MediaResult> DeleteAsync(Guid id, long revision, bool force, CancellationToken cancellationToken = default)
    {
        var current = await media.FindAsync(id, cancellationToken);
        if (current is null) return new(MediaOutcome.NotFound);
        var usages = (await ScanUsagesAsync(cancellationToken)).GetValueOrDefault(id) ?? [];
        if (usages.Count > 0 && !force) return new(MediaOutcome.InUse, null, usages);
        return await media.DeleteAsync(id, revision, cancellationToken) switch
        {
            MediaWriteResult.Success => new(MediaOutcome.Success, null, usages),
            MediaWriteResult.VersionConflict => new(MediaOutcome.Conflict),
            _ => new(MediaOutcome.NotFound)
        };
    }

    /// <summary>
    /// Public access follows the pages that use the media: it is served when a published page that is
    /// available in the invoked context uses it, and protected pages need the PIN grant.
    /// </summary>
    public async Task<PublicMediaResolution> OpenPublicAsync(
        Guid id,
        string? contextId,
        string? profile,
        string? device,
        bool hasProtectedAccess,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!isAdministrator && !await contexts.IsKnownInvocationAsync(contextId, profile, device, cancellationToken))
            return new(PublicMediaResult.NotFound, null, false);
        if (await media.FindAsync(id, cancellationToken) is null) return new(PublicMediaResult.NotFound, null, false);

        var isProtected = false;
        if (!isAdministrator)
        {
            var users = (await ScanUsagesAsync(cancellationToken)).GetValueOrDefault(id) ?? [];
            var available = new List<(MediaUsage Usage, bool Protected)>();
            foreach (var usage in users.Where(usage => usage.IsPublished))
            {
                var page = await pages.FindAsync(usage.PageId, cancellationToken);
                if (page is { IsPublished: true } &&
                    (page.Contexts.Count == 0 || page.Contexts.Contains(contextId!, StringComparer.Ordinal)))
                    available.Add((usage, page.RequiresAuthentication));
            }
            if (available.Count == 0) return new(PublicMediaResult.NotFound, null, false);
            isProtected = available.All(item => item.Protected);
            if (isProtected && !hasProtectedAccess) return new(PublicMediaResult.RequiresPin, null, true);
        }

        var file = await media.OpenAsync(id, cancellationToken);
        return file is null
            ? new(PublicMediaResult.NotFound, null, false)
            : new(PublicMediaResult.Found, file, isProtected);
    }

    private async Task<AdminMediaDetail> DetailAsync(MediaItem item, CancellationToken cancellationToken)
    {
        var usages = (await ScanUsagesAsync(cancellationToken)).GetValueOrDefault(item.Id) ?? [];
        return new AdminMediaDetail(ToAdmin(item, usages.Count), usages);
    }

    /// <summary>Pages whose Markdown links to each media. The Markdown files stay the single source of truth.</summary>
    private async Task<Dictionary<Guid, List<MediaUsage>>> ScanUsagesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, List<MediaUsage>>();
        foreach (var page in await pages.ListAsync(cancellationToken))
        {
            foreach (var id in ReferencedMedia(page.Markdown))
            {
                if (!result.TryGetValue(id, out var list)) result[id] = list = [];
                list.Add(new MediaUsage(page.Id, page.Title, page.Slug, page.IsPublished));
            }
        }
        return result;
    }

    public static IReadOnlyCollection<Guid> ReferencedMedia(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return [];
        var ids = new HashSet<Guid>();
        foreach (Match match in ReferencePattern().Matches(markdown))
            if (Guid.TryParse(match.Groups[1].Value, out var id)) ids.Add(id);
        return ids;
    }

    public static string UrlOf(Guid id) => PublicPath + id.ToString();

    public static string MarkdownFor(MediaItem item)
    {
        var url = UrlOf(item.Id);
        return item.Kind == "image"
            ? $"![{EscapeLabel(item.AltText)}]({url})"
            : $"[{EscapeLabel(item.Title.Length > 0 ? item.Title : item.Name)}]({url})";
    }

    private static AdminMediaItem ToAdmin(MediaItem item, int usageCount) => new(
        item.Id, item.Name, item.Kind, item.ContentType, item.Size, item.Title, item.AltText, item.Folder,
        item.CreatedAt, item.Revision, UrlOf(item.Id), MarkdownFor(item), usageCount);

    private static string EscapeLabel(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string? CleanName(string? fileName, string contentType)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (name.Length == 0) name = "media" + ExtensionOf(contentType);
        if (name.Length > 128 || name.Any(char.IsControl)) return null;
        return name;
    }

    private static string ExtensionOf(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".pdf"
    };

    private static (string Title, string AltText, string Folder)? CleanFields(string? title, string? altText, string? folder)
    {
        var cleanTitle = (title ?? string.Empty).Trim();
        var cleanAlt = (altText ?? string.Empty).Trim();
        var cleanFolder = (folder ?? string.Empty).Trim();
        if (cleanTitle.Length > 200 || cleanAlt.Length > 300 || cleanFolder.Length > 64) return null;
        if (cleanTitle.Concat(cleanAlt).Concat(cleanFolder).Any(char.IsControl)) return null;
        return (cleanTitle, cleanAlt, cleanFolder);
    }

    [GeneratedRegex(
        @"/api/public/media/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
