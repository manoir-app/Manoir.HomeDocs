using System.Text.RegularExpressions;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum PublicPageResult
{
    Found,
    Redirect,
    RequiresPin,
    NotFound
}

/// <summary>The invocation used when an address names no context or profile.</summary>
public sealed record PublicDefaults(string Context, string Profile);

public sealed record PublicPageResolution(PublicPageResult Result, PublicContentPage? Page, string? RedirectSlug);

public sealed partial class UsageContextLogic(
    IUsageContextOperations contexts,
    IContentPageOperations pages,
    IPageTreeOperations tree)
{
    public const string DefaultContextId = "maison";
    public const string DefaultContextName = "Maison";
    public const string DefaultProfile = "desktop";

    private static readonly HashSet<string> Profiles =
        ["desktop", "mobile", "tablet-wall", "tablet-dock", "tv"];

    public Task<IReadOnlyList<UsageContext>> ListAsync(CancellationToken cancellationToken = default) =>
        contexts.ListAsync(cancellationToken);

    /// <summary>
    /// The context and profile to use when none is given: the context "maison" (created at the first start),
    /// or else the first context by name. Null when there is no context at all.
    /// </summary>
    public async Task<PublicDefaults?> GetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var all = await contexts.ListAsync(cancellationToken);
        var chosen = all.FirstOrDefault(item => item.Id == DefaultContextId) ?? all.FirstOrDefault();
        return chosen is null ? null : new PublicDefaults(chosen.Id, DefaultProfile);
    }

    public Task<UsageContext?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        contexts.FindAsync(id, cancellationToken);

    public async Task<ContextWriteResult> SaveAsync(
        string? currentId,
        string? id,
        string? name,
        Guid? rootNodeId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = id?.Trim();
        var normalizedName = name?.Trim();
        if (normalizedId is null || !ContextIdPattern().IsMatch(normalizedId) ||
            normalizedName is null || normalizedName.Length is 0 or > 128 ||
            normalizedName.Any(char.IsControl))
            return ContextWriteResult.InvalidInput;

        return await contexts.SaveAsync(
            currentId,
            new UsageContext(normalizedId, normalizedName, rootNodeId),
            cancellationToken);
    }

    public Task<ContextWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        contexts.DeleteAsync(id, cancellationToken);

    public async Task<bool> IsKnownInvocationAsync(
        string? contextId,
        string? profile,
        string? device,
        CancellationToken cancellationToken = default) =>
        !string.IsNullOrEmpty(contextId) &&
        Profiles.Contains(profile ?? string.Empty) &&
        IsValidDevice(device) &&
        await contexts.FindAsync(contextId, cancellationToken) is not null;

    public async Task<PublicPageResolution> ResolvePageAsync(
        string? contextId,
        string? slug,
        string? profile,
        string? device,
        bool hasProtectedAccess,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contextId) || string.IsNullOrEmpty(slug) ||
            !Profiles.Contains(profile ?? string.Empty) || !IsValidDevice(device))
            return new(PublicPageResult.NotFound, null, null);
        var usageContext = await contexts.FindAsync(contextId, cancellationToken);
        if (usageContext is null) return new(PublicPageResult.NotFound, null, null);

        var pageId = await pages.ResolvePublishedSlugAsync(slug, cancellationToken);
        if (pageId is null) return new(PublicPageResult.NotFound, null, null);
        var page = await pages.FindAsync(pageId.Value, cancellationToken);
        if (page is null || !page.IsPublished)
            return new(PublicPageResult.NotFound, null, null);

        if (!IsAvailable(page, contextId))
        {
            var rootPage = await GetRootPageAsync(usageContext, contextId, cancellationToken);
            return rootPage is null
                ? new(PublicPageResult.NotFound, null, null)
                : new(PublicPageResult.Redirect, null, rootPage.Slug);
        }

        if (page.RequiresAuthentication && !hasProtectedAccess)
            return new(PublicPageResult.RequiresPin, null, null);

        return page.Slug != slug
            ? new(PublicPageResult.Redirect, null, page.Slug)
            : new(PublicPageResult.Found, ToPublicPage(page), null);
    }

    public async Task<PublicNavigation?> GetNavigationAsync(
        string? contextId,
        string? profile,
        string? device,
        bool hasProtectedAccess,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contextId) || !Profiles.Contains(profile ?? string.Empty) || !IsValidDevice(device))
            return null;
        var usageContext = await contexts.FindAsync(contextId, cancellationToken);
        if (usageContext is null) return null;

        var allNodes = await tree.ListAsync(cancellationToken);
        // The default context follows the first root of the tree until a root is chosen for it.
        var rootNode = usageContext.RootNodeId is { } rootId
            ? allNodes.SingleOrDefault(node => node.Id == rootId)
            : usageContext.Id == DefaultContextId
                ? allNodes.Where(node => node.ParentNodeId is null && !node.IsLink)
                    .OrderBy(node => node.Position).ThenBy(node => node.Id).FirstOrDefault()
                : null;
        if (rootNode is null) return null;
        var allPages = (await pages.ListAsync(cancellationToken)).ToDictionary(page => page.Id);
        if (!allPages.TryGetValue(rootNode.PageId, out var rootPage) ||
            !rootPage.IsPublished || !IsAvailable(rootPage, contextId))
            return null;

        var nodesByParent = allNodes.GroupBy(node => node.ParentNodeId ?? Guid.Empty)
            .ToDictionary(group => group.Key, group => group.OrderBy(node => node.Position).ThenBy(node => node.Id).ToArray());
        var resolving = new HashSet<Guid> { rootNode.Id };
        var children = rootPage.RequiresAuthentication && !hasProtectedAccess
            ? []
            : BuildChildren(rootNode.Id);
        var root = new PublicNavigationItem(
            rootNode.Id,
            rootPage.Id,
            rootPage.Title,
            rootPage.Slug,
            rootNode.IsLink,
            rootPage.RequiresAuthentication,
            children);
        return new PublicNavigation(contextId, usageContext.Name, profile!, device, root, children);

        IReadOnlyList<PublicNavigationItem> BuildChildren(Guid parentNodeId)
        {
            if (!nodesByParent.TryGetValue(parentNodeId, out var childNodes))
                return [];
            var result = new List<PublicNavigationItem>(childNodes.Length);
            foreach (var node in childNodes)
            {
                if (!resolving.Add(node.Id))
                    throw new InvalidDataException("A cycle was found in the stored page tree.");
                if (allPages.TryGetValue(node.PageId, out var page) &&
                    page.IsPublished && IsAvailable(page, contextId))
                {
                    result.Add(new PublicNavigationItem(
                        node.Id,
                        page.Id,
                        page.Title,
                        page.Slug,
                        node.IsLink,
                        page.RequiresAuthentication,
                        page.RequiresAuthentication && !hasProtectedAccess
                            ? []
                            : BuildChildren(node.Id)));
                }
                resolving.Remove(node.Id);
            }
            return result;
        }
    }

    private async Task<ContentPage?> GetRootPageAsync(
        UsageContext context,
        string contextId,
        CancellationToken cancellationToken)
    {
        if (context.RootNodeId is null) return null;
        var rootPageId = await tree.ResolveTargetPageAsync(context.RootNodeId.Value, cancellationToken);
        if (rootPageId is null) return null;
        var rootPage = await pages.FindAsync(rootPageId.Value, cancellationToken);
        return rootPage is not null && rootPage.IsPublished && IsAvailable(rootPage, contextId) ? rootPage : null;
    }

    private static bool IsAvailable(ContentPage page, string contextId) =>
        page.Contexts.Count == 0 || page.Contexts.Contains(contextId, StringComparer.Ordinal);

    private static bool IsValidDevice(string? device) =>
        device is null ||
        (device.Length is > 0 and <= 128 &&
         device.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'));

    private static PublicContentPage ToPublicPage(ContentPage page) =>
        new(page.Id, page.Title, page.Slug, page.Category, page.Contexts, page.Markdown, page.RequiresAuthentication);

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContextIdPattern();
}
