using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Operations;

public enum PageTreeResult
{
    Success,
    PageNotFound,
    NodeNotFound,
    ParentNotFound,
    HasChildren,
    CannotRemovePageNode,
    RootInUse,
    InvalidMove
}

public interface IPageTreeOperations
{
    Task<IReadOnlyList<PageTreeNode>> ListAsync(CancellationToken cancellationToken = default);
    Task<Guid?> ResolveTargetPageAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<PageTreeResult> CreateLinkAsync(Guid pageId, Guid? parentNodeId, CancellationToken cancellationToken = default);
    Task<PageTreeResult> MoveAsync(Guid nodeId, Guid? parentNodeId, int position, CancellationToken cancellationToken = default);
    Task<PageTreeResult> RemoveAsync(Guid nodeId, CancellationToken cancellationToken = default);
}
