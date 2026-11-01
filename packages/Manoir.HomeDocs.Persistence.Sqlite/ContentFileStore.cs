using System.Text;
using Microsoft.Extensions.Logging;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class ContentFileStore
{
    private readonly string root;
    private readonly ILogger<ContentFileStore> logger;

    public ContentFileStore(string databasePath, ILogger<ContentFileStore> logger)
    {
        var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new ArgumentException("Database path has no directory.", nameof(databasePath));
        root = Path.Combine(Directory.GetParent(databaseDirectory)?.FullName
            ?? throw new ArgumentException("Database directory has no parent.", nameof(databasePath)), "content");
        this.logger = logger;
    }

    public async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(Path.Combine(root, ".homedocs.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private string PathFor(string fileId)
    {
        if (!Guid.TryParseExact(fileId, "N", out var id) || id.ToString("N") != fileId)
            throw new InvalidDataException("Invalid content file identifier in SQLite.");
        return Path.Combine(root, fileId + ".md");
    }

    public Task<string> ReadAsync(string fileId, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(PathFor(fileId), Encoding.UTF8, cancellationToken);

    public void EnsureExists(string fileId)
    {
        using var file = new FileStream(PathFor(fileId), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Delete(string fileId)
    {
        var path = PathFor(fileId);
        if (!File.Exists(path)) throw new FileNotFoundException("Referenced content file is missing.", path);
        File.Delete(path);
    }

    public void EnsureConsistent(IReadOnlyCollection<string> fileIds)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var referenced = new HashSet<string>(fileIds.Select(PathFor), comparer);
        foreach (var path in referenced)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        var orphans = new List<string>();
        var quarantine = Path.Combine(root, "quarantine");
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Unexpected link in content directory: {path}");
            if (name == "quarantine" && (attributes & FileAttributes.Directory) != 0) continue;
            if (name == ".homedocs.lock" && (attributes & FileAttributes.Directory) == 0) continue;
            if ((attributes & FileAttributes.Directory) != 0 || !IsGeneratedFileName(name))
                throw new InvalidDataException($"Unexpected content entry requires investigation: {path}");
            if (!referenced.Contains(path)) orphans.Add(path);
        }

        if (orphans.Count == 0) return;
        try
        {
            Directory.CreateDirectory(quarantine);
            foreach (var path in orphans)
            {
                var name = Path.GetFileName(path);
                var destination = Path.Combine(quarantine, name);
                while (true)
                {
                    if (File.Exists(destination) || Directory.Exists(destination))
                        destination = Path.Combine(quarantine, $"{Guid.NewGuid():N}-{name}");
                    logger.LogWarning("Quarantining unreferenced HomeDocs content file {Source} to {Destination}.", path, destination);
                    try
                    {
                        File.Move(path, destination);
                        break;
                    }
                    catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
                    {
                        destination = Path.Combine(quarantine, $"{Guid.NewGuid():N}-{name}");
                    }
                }
            }
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Unable to quarantine unreferenced HomeDocs content files in {Root}.", root);
            throw;
        }
    }

    private static bool IsGeneratedFileName(string name)
    {
        var suffix = name.EndsWith(".md.tmp", StringComparison.Ordinal) ? ".md.tmp" : ".md";
        if (!name.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var id = name[..^suffix.Length];
        return Guid.TryParseExact(id, "N", out var guid) && guid.ToString("N") == id;
    }

    public async Task<string> WriteAsync(string markdown, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var finalPath = PathFor(id);
        var staging = finalPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(markdown);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(staging, finalPath);
            return id;
        }
        catch (Exception error)
        {
            if (File.Exists(staging))
                logger.LogWarning(error, "Incomplete HomeDocs content file {Staging} was retained for quarantine.", staging);
            throw;
        }
    }
}
