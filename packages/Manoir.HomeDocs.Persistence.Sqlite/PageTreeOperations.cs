using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class PageTreeOperations(HomeDocsDbContext context, ContentFileStore files) : IPageTreeOperations
{
    public async Task<IReadOnlyList<PageTreeNode>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.PageTreeNodes.AsNoTracking()
            .OrderBy(node => node.ParentNodeId)
            .ThenBy(node => node.Position)
            .ThenBy(node => node.Id)
            .Select(node => new PageTreeNode(
                node.Id,
                node.ParentNodeId,
                node.PageId,
                node.Page.Title,
                node.Page.Slug,
                node.IsLink,
                node.Position))
            .ToListAsync(cancellationToken);

    public Task<Guid?> ResolveTargetPageAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        context.PageTreeNodes.AsNoTracking()
            .Where(node => node.Id == nodeId)
            .Select(node => (Guid?)node.PageId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PageTreeResult> CreateLinkAsync(Guid pageId, Guid? parentNodeId, CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await context.ContentPages.AnyAsync(page => page.Id == pageId, cancellationToken))
            return PageTreeResult.PageNotFound;
        if (!await ParentExistsAsync(parentNodeId, cancellationToken))
            return PageTreeResult.ParentNotFound;

        var position = await context.PageTreeNodes.CountAsync(node => node.ParentNodeId == parentNodeId, cancellationToken);
        context.PageTreeNodes.Add(new PageTreeNodeEntity
        {
            Id = Guid.NewGuid(),
            ParentNodeId = parentNodeId,
            PageId = pageId,
            IsLink = true,
            Position = position
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PageTreeResult.Success;
    }

    public async Task<PageTreeResult> MoveAsync(Guid nodeId, Guid? parentNodeId, int position, CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var nodes = await context.PageTreeNodes.ToListAsync(cancellationToken);
        var node = nodes.SingleOrDefault(item => item.Id == nodeId);
        if (node is null) return PageTreeResult.NodeNotFound;
        if (!ParentExists(nodes, parentNodeId)) return PageTreeResult.ParentNotFound;
        if (!CanMove(nodes, nodeId, parentNodeId)) return PageTreeResult.InvalidMove;

        var oldParentId = node.ParentNodeId;
        var oldSiblings = nodes.Where(item => item.ParentNodeId == oldParentId && item.Id != nodeId)
            .OrderBy(item => item.Position).ThenBy(item => item.Id).ToList();
        var newSiblings = oldParentId == parentNodeId
            ? oldSiblings
            : nodes.Where(item => item.ParentNodeId == parentNodeId)
                .OrderBy(item => item.Position).ThenBy(item => item.Id).ToList();
        var targetIndex = Math.Clamp(position, 0, newSiblings.Count);
        newSiblings.Insert(targetIndex, node);
        node.ParentNodeId = parentNodeId;
        SetPositions(newSiblings);
        if (oldParentId != parentNodeId) SetPositions(oldSiblings);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PageTreeResult.Success;
    }

    public async Task<PageTreeResult> RemoveAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var node = await context.PageTreeNodes.SingleOrDefaultAsync(item => item.Id == nodeId, cancellationToken);
        if (node is null) return PageTreeResult.NodeNotFound;
        if (await context.PageTreeNodes.AnyAsync(child => child.ParentNodeId == nodeId, cancellationToken))
            return PageTreeResult.HasChildren;
        if (!node.IsLink) return PageTreeResult.CannotRemovePageNode;
        if (await context.UsageContexts.AnyAsync(usageContext => usageContext.RootNodeId == nodeId, cancellationToken))
            return PageTreeResult.RootInUse;

        var parentId = node.ParentNodeId;
        context.PageTreeNodes.Remove(node);
        var siblings = await context.PageTreeNodes
            .Where(item => item.ParentNodeId == parentId)
            .OrderBy(item => item.Position).ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        SetPositions(siblings);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PageTreeResult.Success;
    }

    private Task<bool> ParentExistsAsync(Guid? parentNodeId, CancellationToken cancellationToken) =>
        parentNodeId is null
            ? Task.FromResult(true)
            : context.PageTreeNodes.AnyAsync(node => node.Id == parentNodeId, cancellationToken);

    private static bool ParentExists(IReadOnlyCollection<PageTreeNodeEntity> nodes, Guid? parentNodeId) =>
        parentNodeId is null || nodes.Any(node => node.Id == parentNodeId);

    private static bool CanMove(IReadOnlyCollection<PageTreeNodeEntity> nodes, Guid nodeId, Guid? parentNodeId)
    {
        var byId = nodes.ToDictionary(node => node.Id);
        var visited = new HashSet<Guid>();
        var current = parentNodeId;
        while (current is not null)
        {
            if (current == nodeId || !visited.Add(current.Value) || !byId.TryGetValue(current.Value, out var parent))
                return false;
            current = parent.ParentNodeId;
        }
        return true;
    }

    private static void SetPositions(IReadOnlyList<PageTreeNodeEntity> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
            nodes[index].Position = index;
    }
}
