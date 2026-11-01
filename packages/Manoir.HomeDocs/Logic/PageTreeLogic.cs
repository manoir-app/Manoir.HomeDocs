using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public sealed class PageTreeLogic(IPageTreeOperations tree)
{
    public Task<IReadOnlyList<PageTreeNode>> ListAsync(CancellationToken cancellationToken = default) =>
        tree.ListAsync(cancellationToken);

    public Task<Guid?> ResolveTargetPageAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        tree.ResolveTargetPageAsync(nodeId, cancellationToken);

    public Task<PageTreeResult> CreateLinkAsync(Guid pageId, Guid? parentNodeId, CancellationToken cancellationToken = default) =>
        tree.CreateLinkAsync(pageId, parentNodeId, cancellationToken);

    public Task<PageTreeResult> MoveAsync(Guid nodeId, Guid? parentNodeId, int position, CancellationToken cancellationToken = default) =>
        tree.MoveAsync(nodeId, parentNodeId, position, cancellationToken);

    public Task<PageTreeResult> RemoveAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        tree.RemoveAsync(nodeId, cancellationToken);
}
