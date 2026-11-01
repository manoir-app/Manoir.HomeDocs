using System.Collections.Concurrent;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;
using Manoir.HomeDocs.Persistence.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class ContentPagePersistenceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".content-tests", Guid.NewGuid().ToString("N"));
    private ServiceProvider services = null!;
    private readonly CaptureLoggerProvider logs = new();
    private string DatabasePath => Path.Combine(root, "database", "homedocs.db");
    private string ContentPath => Path.Combine(root, "content");
    private string QuarantinePath => Path.Combine(ContentPath, "quarantine");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        services = new ServiceCollection().AddLogging(builder => builder.AddProvider(logs))
            .AddHomeDocsSqlite(_ => DatabasePath).BuildServiceProvider();
        await services.MigrateHomeDocsAsync();
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(ContextWriteResult.Success,
            await scope.ServiceProvider.GetRequiredService<IUsageContextOperations>()
                .SaveAsync(null, new UsageContext("maison", "Maison", null)));
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        foreach (var path in new[] { DatabasePath, Path.Combine(root, "upgrade", "database", "homedocs.db") })
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path }.ToString());
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private async Task<T> Run<T>(Func<IContentPageOperations, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IContentPageOperations>());
    }

    private async Task<T> RunTree<T>(Func<IPageTreeOperations, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPageTreeOperations>());
    }

    private static ContentPage New(string slug, bool published = false) =>
        new(Guid.NewGuid(), "Titre", slug, "page", ["maison"], true, published, "# Bonjour\nété", 1);

    [Fact]
    public async Task MigrationPreservesAdministratorAndInitializesPageSchema()
    {
        var upgradeRoot = Path.Combine(root, "upgrade");
        var db = Path.Combine(upgradeRoot, "database", "homedocs.db");
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        await using var upgrade = new ServiceCollection().AddHomeDocsSqlite(_ => db).BuildServiceProvider();
        await using (var scope = upgrade.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<HomeDocsDbContext>();
            await context.GetService<IMigrator>().MigrateAsync("20261101130817_InitialCreate");
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAdministratorOperations>()
                .TryCreateInitialAsync(new("admin", "ADMIN", "hash")));
        }
        await upgrade.MigrateHomeDocsAsync();
        await using (var scope = upgrade.CreateAsyncScope())
        {
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAdministratorOperations>().FindByNormalizedUserNameAsync("ADMIN"));
            Assert.Equal(3, (await scope.ServiceProvider.GetRequiredService<HomeDocsDbContext>().Database.GetAppliedMigrationsAsync()).Count());
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IPageTreeOperations>().ListAsync());
        }

        var page = New("ma-page");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        var stored = await Run(ops => ops.FindAsync(page.Id));
        Assert.NotNull(stored);
        Assert.Equal(page, stored with { Contexts = page.Contexts });
        Assert.Equal(page.Contexts, stored.Contexts);
        Assert.Equal(page.Markdown, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(ContentPath, "*.md"))));
        Assert.Single(await Run(ops => ops.ListAsync()));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync(page.Slug)));
    }

    [Fact]
    public async Task RenamesRedirectDirectlyAndReuseByDraftPermanentlyRemovesRedirect()
    {
        var page = New("un", true);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { Slug = "deux" }, false)));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { Slug = "trois", Revision = 2 }, false)));
        Assert.Equal(page.Id, await Run(ops => ops.ResolvePublishedSlugAsync("un")));
        Assert.Equal(page.Id, await Run(ops => ops.ResolvePublishedSlugAsync("deux")));
        Assert.Equal(page.Id, await Run(ops => ops.ResolvePublishedSlugAsync("trois")));

        var draft = New("un");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(draft, true)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("un")));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.DeleteAsync(draft.Id, 1)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("un")));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { Slug = "trois", Revision = 3, IsPublished = false }, false)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("deux")));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("trois")));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.DeleteAsync(page.Id, 4)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("deux")));
    }

    [Fact]
    public async Task PublishedSlugSurvivesUnpublicationAndDraftEditsButReuseRemovesIt()
    {
        var page = New("initial", true);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { IsPublished = false }, false)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("initial")));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { IsPublished = false, Revision = 2, Markdown = "draft" }, false)));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { IsPublished = false, Slug = "nouveau", Revision = 3 }, false)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("initial")));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { Slug = "nouveau", Revision = 4 }, false)));
        Assert.Equal(page.Id, await Run(ops => ops.ResolvePublishedSlugAsync("initial")));
        var reuse = New("initial");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(reuse, true)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("initial")));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.DeleteAsync(reuse.Id, 1)));
        Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync("initial")));
    }

    [Fact]
    public async Task ConcurrentCreatesAndUpdatesDoNotOverwriteAndUseOptimisticRevisions()
    {
        var candidates = Enumerable.Range(0, 8).Select(_ => New("collision")).ToArray();
        var results = await Task.WhenAll(candidates.Select(page => Task.Run(() => Run(ops => ops.SaveAsync(page, true)))));
        Assert.Single(results, result => result == PageWriteResult.Success);
        Assert.Equal(7, results.Count(result => result == PageWriteResult.SlugConflict));
        var winner = candidates[Array.IndexOf(results, PageWriteResult.Success)];
        var updates = Enumerable.Range(0, 8).Select(i => winner with { Markdown = $"# version {i}" }).ToArray();
        var updateResults = await Task.WhenAll(updates.Select(page => Task.Run(() => Run(ops => ops.SaveAsync(page, false)))));
        Assert.Single(updateResults, result => result == PageWriteResult.Success);
        Assert.Equal(7, updateResults.Count(result => result == PageWriteResult.VersionConflict));
        var saved = await Run(ops => ops.FindAsync(winner.Id));
        Assert.Equal(2, saved!.Revision);
        Assert.Equal(updates[Array.IndexOf(updateResults, PageWriteResult.Success)].Markdown, saved.Markdown);
        Assert.Single(Directory.GetFiles(ContentPath, "*.md"));
        Assert.Equal(PageWriteResult.VersionConflict, await Run(ops => ops.DeleteAsync(winner.Id, 1)));
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.DeleteAsync(winner.Id, 2)));
        Assert.Null(await Run(ops => ops.FindAsync(winner.Id)));
        Assert.Empty(Directory.GetFiles(ContentPath, "*.md"));
    }

    [Fact]
    public async Task MissingReferencedFileIsReportedWithoutDeletingMetadata()
    {
        var page = New("absent", true);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        File.Delete(Assert.Single(Directory.GetFiles(ContentPath, "*.md")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(ops => ops.FindAsync(page.Id)));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(ops => ops.DeleteAsync(page.Id, 1)));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(ops => ops.ResolvePublishedSlugAsync(page.Slug)));
    }

    [Fact]
    public async Task FailedDatabaseWriteQuarantinesUncommittedFileAndPreservesPreviousPage()
    {
        var page = New("stable", true);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<HomeDocsDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER FailContentUpdate BEFORE UPDATE ON ContentPages BEGIN SELECT RAISE(ABORT, 'test failure'); END");
        }
        await Assert.ThrowsAnyAsync<Exception>(() => Run(ops => ops.SaveAsync(page with { Markdown = "changed" }, false)));
        Assert.Single(Directory.GetFiles(ContentPath, "*.md"));
        Assert.Equal("changed", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(QuarantinePath))));
        Assert.Equal(page.Markdown, (await Run(ops => ops.FindAsync(page.Id)))!.Markdown);
    }

    [Fact]
    public async Task UnexpectedFilesAndInvalidSlugsAreReported()
    {
        var page = New("../escape");
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ops => ops.SaveAsync(page, true)));
        Directory.CreateDirectory(ContentPath);
        await File.WriteAllTextAsync(Path.Combine(ContentPath, "unexpected.md"), "unexpected");
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(ops => ops.ListAsync()));
    }

    [Fact]
    public async Task RacingDeleteAndUpdateHaveExactlyOneWinner()
    {
        var page = New("race", true);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        var update = Task.Run(() => Run(ops => ops.SaveAsync(page with { Markdown = "nouveau" }, false)));
        var delete = Task.Run(() => Run(ops => ops.DeleteAsync(page.Id, 1)));
        var results = await Task.WhenAll(update, delete);
        Assert.Single(results, result => result == PageWriteResult.Success);
        Assert.Contains(results, result => result is PageWriteResult.NotFound or PageWriteResult.VersionConflict);
        var saved = await Run(ops => ops.FindAsync(page.Id));
        if (saved is not null)
        {
            Assert.Equal(2, saved.Revision);
            Assert.Equal("nouveau", saved.Markdown);
        }
        else Assert.Null(await Run(ops => ops.ResolvePublishedSlugAsync(page.Slug)));
    }

    [Fact]
    public async Task CleanupAndQuarantineFailuresAreReportedAndRecoveryCanBeRetried()
    {
        if (!OperatingSystem.IsWindows()) return;
        var page = New("locked");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        var path = Assert.Single(Directory.GetFiles(ContentPath, "*.md"));
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Assert.ThrowsAsync<IOException>(() => Run(ops => ops.DeleteAsync(page.Id, 1)));
            Assert.Contains("was deleted", error.Message);
            await Assert.ThrowsAsync<IOException>(() => Run(ops => ops.FindAsync(page.Id)));
            Assert.True(File.Exists(path));
        }

        Assert.Null(await Run(ops => ops.FindAsync(page.Id)));
        Assert.Equal(page.Markdown, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(QuarantinePath))));
        Assert.Contains(logs.Messages, message => message.Contains("Unable to quarantine"));
    }

    [Fact]
    public async Task PageTreeSupportsPlacementLinksAndKeyboardReorderingWithoutCopies()
    {
        var parentPage = New("parent");
        var childPage = New("child");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(parentPage, true)));
        var parentNode = Assert.Single(await RunTree(ops => ops.ListAsync()));
        Assert.Equal(parentPage.Id, parentNode.PageId);

        Assert.Equal(PageWriteResult.Success,
            await Run(ops => ops.SaveAsync(childPage, true, parentNodeId: parentNode.Id)));
        var nodes = await RunTree(ops => ops.ListAsync());
        var childNode = Assert.Single(nodes, node => node.PageId == childPage.Id);
        Assert.Equal(parentNode.Id, childNode.ParentNodeId);

        Assert.Equal(PageTreeResult.Success,
            await RunTree(ops => ops.CreateLinkAsync(parentPage.Id, childNode.Id)));
        nodes = await RunTree(ops => ops.ListAsync());
        var link = Assert.Single(nodes, node => node.IsLink);
        Assert.Equal(parentPage.Id, link.PageId);
        Assert.Equal(parentPage.Id, await RunTree(ops => ops.ResolveTargetPageAsync(link.Id)));
        Assert.Equal(2, (await Run(ops => ops.ListAsync())).Count);
        Assert.Equal(PageTreeResult.PageNotFound,
            await RunTree(ops => ops.CreateLinkAsync(Guid.NewGuid(), null)));
        Assert.Equal(PageTreeResult.InvalidMove,
            await RunTree(ops => ops.MoveAsync(parentNode.Id, link.Id, 0)));
        Assert.Equal(PageTreeResult.HasChildren,
            await RunTree(ops => ops.RemoveAsync(parentNode.Id)));
        Assert.Equal(PageTreeResult.Success,
            await RunTree(ops => ops.RemoveAsync(link.Id)));
        Assert.Equal(PageTreeResult.CannotRemovePageNode,
            await RunTree(ops => ops.RemoveAsync(childNode.Id)));
        Assert.Equal(PageWriteResult.Success,
            await Run(ops => ops.DeleteAsync(childPage.Id, 1)));

        var siblingPage = New("sibling");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(siblingPage, true)));
        var siblingNode = Assert.Single(await RunTree(ops => ops.ListAsync()),
            node => node.PageId == siblingPage.Id);
        Assert.Equal(PageTreeResult.Success,
            await RunTree(ops => ops.MoveAsync(siblingNode.Id, null, 0)));
        nodes = await RunTree(ops => ops.ListAsync());
        Assert.Equal(siblingNode.Id, nodes.OrderBy(node => node.Position).First().Id);
        Assert.Equal(PageTreeResult.Success,
            await RunTree(ops => ops.MoveAsync(parentNode.Id, siblingNode.Id, 0)));
        Assert.Equal(PageTreeResult.InvalidMove,
            await RunTree(ops => ops.MoveAsync(siblingNode.Id, parentNode.Id, 0)));
    }

    [Fact]
    public async Task InterruptedCreationRecoversStagedAndFinalFilesWithoutOverwritingQuarantine()
    {
        Directory.CreateDirectory(ContentPath);
        Directory.CreateDirectory(QuarantinePath);
        var id = Guid.NewGuid().ToString("N");
        var final = Path.Combine(ContentPath, id + ".md");
        var stage = final + ".tmp";
        await File.WriteAllTextAsync(final, "final");
        await File.WriteAllTextAsync(stage, "partial");
        await File.WriteAllTextAsync(Path.Combine(QuarantinePath, Path.GetFileName(final)), "existing");
        Assert.Empty(await Run(ops => ops.ListAsync()));
        Assert.False(File.Exists(final));
        Assert.False(File.Exists(stage));
        var contents = await Task.WhenAll(Directory.GetFiles(QuarantinePath).Select(path => File.ReadAllTextAsync(path)));
        Assert.Equal(new[] { "existing", "final", "partial" }, contents.Order().ToArray());
        Assert.True(logs.Messages.Count(message => message.Contains("Quarantining unreferenced")) >= 2);
        Assert.Empty(await Run(ops => ops.ListAsync()));
        Assert.Equal(3, Directory.GetFiles(QuarantinePath).Length);
    }

    [Fact]
    public async Task InterruptedUpdateAndDeleteRecoverUnreferencedVersions()
    {
        var page = New("recuperation", true);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        var oldPath = Assert.Single(Directory.GetFiles(ContentPath, "*.md"));
        var uncommitted = Path.Combine(ContentPath, Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(uncommitted, "nouvelle version non validee");
        Assert.Equal(page.Markdown, (await Run(ops => ops.FindAsync(page.Id)))!.Markdown);

        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page with { Markdown = "version validee" }, false)));
        await File.WriteAllTextAsync(oldPath, page.Markdown);
        Assert.Equal("version validee", (await Run(ops => ops.FindAsync(page.Id)))!.Markdown);
        var current = Assert.Single(Directory.GetFiles(ContentPath, "*.md"));
        var currentContent = await File.ReadAllTextAsync(current);
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.DeleteAsync(page.Id, 2)));
        await File.WriteAllTextAsync(current, currentContent);
        Assert.Null(await Run(ops => ops.FindAsync(page.Id)));
        var contents = await Task.WhenAll(Directory.GetFiles(QuarantinePath).Select(path => File.ReadAllTextAsync(path)));
        Assert.Equal(new[] { page.Markdown, "nouvelle version non validee", "version validee" }.Order().ToArray(), contents.Order().ToArray());
    }

    [Fact]
    public async Task QuarantineFailureAndMissingReferencesRemainExplicit()
    {
        var page = New("manquant");
        Assert.Equal(PageWriteResult.Success, await Run(ops => ops.SaveAsync(page, true)));
        var referenced = Assert.Single(Directory.GetFiles(ContentPath, "*.md"));
        var orphan = Path.Combine(ContentPath, Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(orphan, "orphan");
        File.Delete(referenced);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(ops => ops.ListAsync()));
        Assert.True(File.Exists(orphan));
        await File.WriteAllTextAsync(referenced, page.Markdown);
        await File.WriteAllTextAsync(QuarantinePath, "not a directory");
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(ops => ops.ListAsync()));
        Assert.True(File.Exists(orphan));
        File.Delete(QuarantinePath);
        Assert.Single(await Run(ops => ops.ListAsync()));
        Assert.Equal("orphan", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(QuarantinePath))));
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
