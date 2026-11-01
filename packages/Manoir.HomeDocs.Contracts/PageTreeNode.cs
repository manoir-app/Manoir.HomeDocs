namespace Manoir.HomeDocs.Contracts;

public sealed record PageTreeNode(
    Guid Id,
    Guid? ParentNodeId,
    Guid PageId,
    string Title,
    string Slug,
    bool IsLink,
    int Position);

public sealed record PageTreePlacement(Guid? ParentNodeId);
