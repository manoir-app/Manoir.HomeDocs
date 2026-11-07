using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public sealed class PageLinkLogic(IContentPageOperations pages, IMediaOperations media)
{
    /// <summary>Outgoing links of a page with their status, and the pages that link to it.</summary>
    public async Task<PageLinkReport?> ReportAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        var index = await BuildIndexAsync(cancellationToken);
        if (!index.PagesById.TryGetValue(pageId, out var page)) return null;
        var outgoing = await ResolveAsync(page, index, cancellationToken);
        var incoming = new List<IncomingLink>();
        foreach (var other in index.PagesById.Values.Where(item => item.Id != pageId).OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var links = await ResolveAsync(other, index, cancellationToken);
            if (links.Any(link => link.Kind == "page" && link.TargetId == pageId))
                incoming.Add(new IncomingLink(other.Id, other.Title, other.Slug, other.IsPublished));
        }
        return new PageLinkReport(outgoing, incoming);
    }

    /// <summary>Every link of every page that leads nowhere.</summary>
    public async Task<IReadOnlyList<BrokenLink>> BrokenLinksAsync(CancellationToken cancellationToken = default)
    {
        var index = await BuildIndexAsync(cancellationToken);
        var result = new List<BrokenLink>();
        foreach (var page in index.PagesById.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase))
            foreach (var link in await ResolveAsync(page, index, cancellationToken))
                if (link.Status == "broken") result.Add(new BrokenLink(page.Id, page.Title, page.Slug, link));
        return result;
    }

    private sealed record PageIndex(
        Dictionary<Guid, ContentPage> PagesById,
        Dictionary<string, ContentPage> PagesBySlug);

    private async Task<PageIndex> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var all = await pages.ListAsync(cancellationToken);
        return new PageIndex(all.ToDictionary(page => page.Id), all.ToDictionary(page => page.Slug, StringComparer.Ordinal));
    }

    private async Task<IReadOnlyList<PageLink>> ResolveAsync(ContentPage page, PageIndex index, CancellationToken cancellationToken)
    {
        var result = new List<PageLink>();
        foreach (var link in MarkdownLinks.Extract(page.Markdown))
        {
            switch (link.Kind)
            {
                case LinkTargetKind.External:
                    result.Add(new PageLink(link.Href, "external", "unchecked", null, null));
                    break;
                case LinkTargetKind.Media:
                    var item = await media.FindAsync(link.MediaId!.Value, cancellationToken);
                    result.Add(item is null
                        ? new PageLink(link.Href, "media", "broken", link.MediaId, null)
                        : new PageLink(link.Href, "media", "ok", item.Id, item.Title.Length > 0 ? item.Title : item.Name));
                    break;
                default:
                    result.Add(await ResolvePageAsync(link, index, cancellationToken));
                    break;
            }
        }
        return result;
    }

    private async Task<PageLink> ResolvePageAsync(MarkdownLink link, PageIndex index, CancellationToken cancellationToken)
    {
        if (index.PagesBySlug.TryGetValue(link.Slug!, out var target))
            return new PageLink(link.Href, "page", target.IsPublished ? "ok" : "draft", target.Id, target.Title);
        // A renamed page keeps answering on its former slug: the link still works, but should be updated.
        var redirected = await pages.ResolvePublishedSlugAsync(link.Slug!, cancellationToken);
        return redirected is Guid id && index.PagesById.TryGetValue(id, out var renamed)
            ? new PageLink(link.Href, "page", "redirected", renamed.Id, renamed.Title)
            : new PageLink(link.Href, "page", "broken", null, null);
    }
}
