namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class UsageContextEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public Guid? RootNodeId { get; set; }
    public PageTreeNodeEntity? RootNode { get; set; }
}
