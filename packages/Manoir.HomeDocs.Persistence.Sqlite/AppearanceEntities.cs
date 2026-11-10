namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class ThemeEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string ColorScheme { get; set; }
    public required string DataJson { get; set; }
    public long Revision { get; set; }
}

internal sealed class ProfileSettingsEntity
{
    public required string ProfileId { get; set; }
    public required string DataJson { get; set; }
    public long Revision { get; set; }
}
