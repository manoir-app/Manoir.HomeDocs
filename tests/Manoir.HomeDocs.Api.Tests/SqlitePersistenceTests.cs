using Manoir.HomeDocs.Operations;
using Manoir.HomeDocs.Persistence.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class SqlitePersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-sqlite-{Guid.NewGuid():N}");

    [Fact]
    public async Task MigrationAndOperationsPersistAndReadTheInitialAdministrator()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(),
            migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));

        var operations = new AdministratorOperations(context);
        Assert.False(await operations.IsInitializedAsync());
        var credentials = new AdministratorCredentials("admin", "ADMIN", "test-hash");
        Assert.True(await operations.TryCreateInitialAsync(credentials));
        Assert.False(await operations.TryCreateInitialAsync(new("other", "OTHER", "another-hash")));
        Assert.True(await operations.IsInitializedAsync());
        Assert.Equal(credentials, await operations.FindByNormalizedUserNameAsync("ADMIN"));
        Assert.Null(await operations.FindByNormalizedUserNameAsync("OTHER"));
    }

    [Fact]
    public async Task AdministratorSettingsPersistLanguageAndPinHashWithoutPlaintextPin()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var operations = new AdministratorOperations(context);
        Assert.True(await operations.TryCreateInitialAsync(new("admin", "ADMIN", "password-hash")));
        Assert.True(await operations.UpdateLanguageAsync("ADMIN", "en"));
        Assert.True(await operations.UpdatePinHashAsync("ADMIN", "pin-password-hash"));

        var stored = await operations.FindByNormalizedUserNameAsync("ADMIN");
        Assert.NotNull(stored);
        Assert.Equal("en", stored.Language);
        Assert.Equal("pin-password-hash", stored.PinHash);
    }

    [Fact]
    public async Task ConcurrentInitializationCreatesAtMostOneAdministrator()
    {
        await using (var setupContext = CreateContext())
        {
            await setupContext.Database.MigrateAsync();
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            await start.Task;
            await using var context = CreateContext();
            var operations = new AdministratorOperations(context);
            return await operations.TryCreateInitialAsync(new($"admin{index}", $"ADMIN{index}", "test-hash"));
        })).ToArray();
        start.SetResult();

        var results = await Task.WhenAll(attempts);
        Assert.Single(results, created => created);
        await using var verification = CreateContext();
        Assert.True(await new AdministratorOperations(verification).IsInitializedAsync());
    }

    private HomeDocsDbContext CreateContext()
    {
        Directory.CreateDirectory(root);
        var options = new DbContextOptionsBuilder<HomeDocsDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
        return new HomeDocsDbContext(options);
    }

    public void Dispose()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "test.db")}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
