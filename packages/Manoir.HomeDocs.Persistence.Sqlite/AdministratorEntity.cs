namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class AdministratorEntity
{
    public int Id { get; set; }
    public required string UserName { get; set; }
    public required string NormalizedUserName { get; set; }
    public required string PasswordHash { get; set; }
}
