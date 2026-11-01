using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class ContentPageOperations(HomeDocsDbContext context, ContentFileStore files) : IContentPageOperations
{
    private static void Validate(ContentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ContentPageValidation.IsValid(page))
            throw new ArgumentException("Invalid page metadata or Markdown.", nameof(page));
    }

    public async Task<IReadOnlyList<ContentPage>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        await EnsureConsistencyAsync(cancellationToken);
        var rows = await context.ContentPages.AsNoTracking().OrderBy(page => page.Title).ThenBy(page => page.Id).ToListAsync(cancellationToken);
        var result = new List<ContentPage>(rows.Count);
        foreach (var row in rows) result.Add(await MaterializeAsync(row, cancellationToken));
        return result;
    }

    public async Task<ContentPage?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        await EnsureConsistencyAsync(cancellationToken);
        var row = await context.ContentPages.AsNoTracking().SingleOrDefaultAsync(page => page.Id == id, cancellationToken);
        return row is null ? null : await MaterializeAsync(row, cancellationToken);
    }

    private async Task<ContentPage> MaterializeAsync(ContentPageEntity row, CancellationToken cancellationToken)
    {
        var contexts = JsonSerializer.Deserialize<string[]>(row.ContextsJson)
            ?? throw new InvalidDataException("Invalid stored contexts.");
        var page = new ContentPage(row.Id, row.Title, row.Slug, row.Category, contexts,
            row.RequiresAuthentication, row.IsPublished, await files.ReadAsync(row.FileId, cancellationToken), row.Revision);
        Validate(page);
        if (page.Revision <= 0) throw new InvalidDataException("Invalid stored revision.");
        return page;
    }

    private async Task EnsureConsistencyAsync(CancellationToken cancellationToken) =>
        files.EnsureConsistent(await context.ContentPages.AsNoTracking().Select(page => page.FileId).ToListAsync(cancellationToken));

    public async Task<PageWriteResult> SaveAsync(
        ContentPage page,
        bool isNew,
        CancellationToken cancellationToken = default,
        Guid? parentNodeId = null)
    {
        Validate(page);
        await using var gate = await files.LockAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await EnsureConsistencyAsync(cancellationToken);
        var existing = await context.ContentPages.SingleOrDefaultAsync(item => item.Id == page.Id, cancellationToken);
        if (isNew && existing is not null) return PageWriteResult.VersionConflict;
        if (!isNew && existing is null) return PageWriteResult.NotFound;
        if (!isNew && existing!.Revision != page.Revision) return PageWriteResult.VersionConflict;
        if (isNew && parentNodeId is not null &&
            !await context.PageTreeNodes.AnyAsync(node => node.Id == parentNodeId, cancellationToken))
            return PageWriteResult.InvalidParent;
        if (page.Contexts.Count > 0 &&
            await context.UsageContexts.CountAsync(usageContext => page.Contexts.Contains(usageContext.Id), cancellationToken) != page.Contexts.Count)
            return PageWriteResult.InvalidContext;
        if (existing is not null && page.Contexts.Count > 0)
        {
            var contextRoots = await context.UsageContexts.AsNoTracking()
                .Where(usageContext => usageContext.RootNodeId != null &&
                    context.PageTreeNodes.Any(node => node.Id == usageContext.RootNodeId && node.PageId == page.Id))
                .Select(usageContext => usageContext.Id)
                .ToListAsync(cancellationToken);
            if (contextRoots.Any(contextId => !page.Contexts.Contains(contextId, StringComparer.Ordinal)))
                return PageWriteResult.ContextRootInUse;
        }
        if (await context.ContentPages.AnyAsync(item => item.Slug == page.Slug && item.Id != page.Id, cancellationToken))
            return PageWriteResult.SlugConflict;
        if (existing is not null) files.EnsureExists(existing.FileId);

        var oldFile = existing?.FileId;
        var oldSlug = existing?.Slug;
        var redirectOldSlug = existing is { IsPublished: true } && (oldSlug != page.Slug || !page.IsPublished);
        var revision = isNew ? 1 : checked(existing!.Revision + 1);
        var fileId = await files.WriteAsync(page.Markdown, cancellationToken);

        var commitStarted = false;
        try
        {
            if (existing is null || oldSlug != page.Slug)
            {
                var reused = await context.ContentPageRedirects.SingleOrDefaultAsync(item => item.Slug == page.Slug, cancellationToken);
                if (reused is not null) context.ContentPageRedirects.Remove(reused);
            }
            if (redirectOldSlug)
            {
                var redirect = await context.ContentPageRedirects.SingleOrDefaultAsync(item => item.Slug == oldSlug, cancellationToken);
                if (redirect is null) context.ContentPageRedirects.Add(new ContentPageRedirectEntity { Slug = oldSlug!, PageId = page.Id });
                else redirect.PageId = page.Id;
            }
            var target = existing ?? new ContentPageEntity { Id = page.Id };
            target.Title = page.Title;
            target.Slug = page.Slug;
            target.Category = page.Category;
            target.ContextsJson = JsonSerializer.Serialize(page.Contexts);
            target.RequiresAuthentication = page.RequiresAuthentication;
            target.IsPublished = page.IsPublished;
            target.FileId = fileId;
            target.Revision = revision;
            if (existing is null)
            {
                context.ContentPages.Add(target);
                var position = await context.PageTreeNodes.CountAsync(
                    node => node.ParentNodeId == parentNodeId, cancellationToken);
                context.PageTreeNodes.Add(new PageTreeNodeEntity
                {
                    Id = Guid.NewGuid(),
                    ParentNodeId = parentNodeId,
                    PageId = page.Id,
                    IsLink = false,
                    Position = position
                });
            }
            await context.SaveChangesAsync(cancellationToken);
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error)
        {
            if (commitStarted)
                throw new InvalidOperationException($"Commit outcome uncertain; inspect content file {fileId}.md and SQLite before retrying.", error);
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await EnsureConsistencyAsync(CancellationToken.None);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException($"Rollback or cleanup failed; inspect content file {fileId}.md and SQLite.", error, cleanup);
            }
            throw;
        }
        if (oldFile is not null)
        {
            try { files.Delete(oldFile); }
            catch (Exception error) { throw new IOException($"Page was saved, but old content file {oldFile}.md could not be removed.", error); }
        }
        return PageWriteResult.Success;
    }

    public async Task<PageWriteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await EnsureConsistencyAsync(cancellationToken);
        var page = await context.ContentPages.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (page is null) return PageWriteResult.NotFound;
        if (page.Revision != revision) return PageWriteResult.VersionConflict;
        var pageNodes = await context.PageTreeNodes.Where(node => node.PageId == id).ToListAsync(cancellationToken);
        var pageNodeIds = pageNodes.Select(node => node.Id).ToHashSet();
        if (pageNodeIds.Count > 0 && await context.PageTreeNodes.AnyAsync(
            child => child.ParentNodeId != null && pageNodeIds.Contains(child.ParentNodeId.Value), cancellationToken))
            return PageWriteResult.HasChildren;
        if (pageNodeIds.Count > 0 && await context.UsageContexts.AnyAsync(
            usageContext => usageContext.RootNodeId != null && pageNodeIds.Contains(usageContext.RootNodeId.Value), cancellationToken))
            return PageWriteResult.ContextRootInUse;
        files.EnsureExists(page.FileId);
        var affectedParents = pageNodes.Select(node => node.ParentNodeId).Distinct().ToArray();
        context.PageTreeNodes.RemoveRange(pageNodes);
        context.ContentPages.Remove(page);
        foreach (var parentId in affectedParents)
        {
            var siblings = await context.PageTreeNodes
                .Where(node => node.ParentNodeId == parentId && !pageNodeIds.Contains(node.Id))
                .OrderBy(node => node.Position)
                .ToListAsync(cancellationToken);
            for (var index = 0; index < siblings.Count; index++)
                siblings[index].Position = index;
        }
        await context.SaveChangesAsync(cancellationToken);
        try { await transaction.CommitAsync(cancellationToken); }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Delete commit outcome uncertain; inspect page {id} and content file {page.FileId}.md before retrying.", error);
        }
        try { files.Delete(page.FileId); }
        catch (Exception error) { throw new IOException($"Page was deleted, but content file {page.FileId}.md could not be removed.", error); }
        return PageWriteResult.Success;
    }

    public async Task<Guid?> ResolvePublishedSlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!ContentPageValidation.IsSlug(slug)) return null;
        await using var gate = await files.LockAsync(cancellationToken);
        await EnsureConsistencyAsync(cancellationToken);
        var current = await context.ContentPages.AsNoTracking()
            .Where(page => page.Slug == slug).Select(page => new { page.Id, page.IsPublished }).SingleOrDefaultAsync(cancellationToken);
        if (current is not null) return current.IsPublished ? current.Id : null;
        return await context.ContentPageRedirects.AsNoTracking()
            .Where(redirect => redirect.Slug == slug && redirect.Page.IsPublished)
            .Select(redirect => (Guid?)redirect.PageId).SingleOrDefaultAsync(cancellationToken);
    }
}
