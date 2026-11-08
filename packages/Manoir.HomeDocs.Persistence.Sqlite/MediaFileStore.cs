namespace Manoir.HomeDocs.Persistence.Sqlite;

/// <summary>Media bytes, stored as {id}.bin next to the database directory. Names come from SQLite ids only.</summary>
internal sealed class MediaFileStore
{
    private readonly string root;

    public MediaFileStore(string databasePath)
    {
        var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new ArgumentException("Database path has no directory.", nameof(databasePath));
        root = Path.Combine(Directory.GetParent(databaseDirectory)?.FullName
            ?? throw new ArgumentException("Database directory has no parent.", nameof(databasePath)), "media");
    }

    private string PathFor(Guid id) => Path.Combine(root, id.ToString("N") + ".bin");

    public async Task WriteAsync(Guid id, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var temporary = PathFor(id) + ".tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, PathFor(id), overwrite: false);
    }

    public Stream Open(Guid id) =>
        new FileStream(PathFor(id), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }
}
