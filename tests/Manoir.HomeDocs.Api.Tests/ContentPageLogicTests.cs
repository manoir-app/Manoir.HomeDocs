using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class ContentPageLogicTests
{
    private static ContentPageInput Input(string slug = "cuisine") =>
        new(" Cuisine ", slug, "piece", ["invites"], true, "# Cuisine\n");

    [Fact]
    public async Task CreateAndDuplicateProduceDraftsWithStableIndependentIds()
    {
        var storage = new StubPages();
        var logic = new ContentPageLogic(storage);
        var (result, id) = await logic.CreateAsync(Input());
        Assert.Equal(ContentPageResult.Success, result);
        var source = await logic.FindAsync(id);
        Assert.NotNull(source);
        Assert.Equal("Cuisine", source.Title);
        Assert.False(source.IsPublished);
        Assert.True(source.RequiresAuthentication);
        Assert.Equal(ContentPageResult.Success, await logic.SetPublicationAsync(id, 1, true));

        var (duplicateResult, copyId) = await logic.DuplicateAsync(id, "Copie", "copie");
        Assert.Equal(ContentPageResult.Success, duplicateResult);
        Assert.NotEqual(id, copyId);
        var copy = await logic.FindAsync(copyId);
        Assert.NotNull(copy);
        Assert.False(copy.IsPublished);
        Assert.Equal("# Cuisine\n", copy.Markdown);
        Assert.Equal(source.Contexts, copy.Contexts);
        Assert.True(copy.RequiresAuthentication);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../fichier")]
    [InlineData("Page")]
    [InlineData("double--tiret")]
    [InlineData("x\n")]
    public async Task InvalidSlugIsRejectedBeforeStorage(string slug)
    {
        var storage = new StubPages();
        var logic = new ContentPageLogic(storage);
        Assert.Equal(ContentPageResult.InvalidInput, (await logic.CreateAsync(Input(slug))).Result);
        Assert.Empty(storage.Items);
    }

    [Fact]
    public async Task ValidationAndOptimisticConcurrencyProtectChanges()
    {
        var storage = new StubPages();
        var logic = new ContentPageLogic(storage);
        Assert.Equal(ContentPageResult.InvalidInput, (await logic.CreateAsync(Input() with { Contexts = ["x", "x"] })).Result);
        Assert.Equal(ContentPageResult.InvalidInput, (await logic.CreateAsync(Input() with { Category = "unknown" })).Result);
        Assert.Equal(ContentPageResult.InvalidInput, (await logic.CreateAsync(Input() with { RequiresAuthentication = null })).Result);
        Assert.Equal(ContentPageResult.InvalidInput, (await logic.CreateAsync(Input() with { Markdown = new string('a', 1024 * 1024 + 1) })).Result);
        var (_, id) = await logic.CreateAsync(Input());

        Assert.Equal(ContentPageResult.Success, await logic.SetPublicationAsync(id, 1, true));
        Assert.Equal(ContentPageResult.VersionConflict, await logic.UpdateAsync(id, 1, Input()));
        Assert.Equal(ContentPageResult.Success, await logic.UpdateAsync(id, 2, Input() with { Markdown = "modifié" }));
        var updated = await logic.FindAsync(id);
        Assert.NotNull(updated);
        Assert.True(updated.IsPublished);
        Assert.Equal("modifié", updated.Markdown);
        Assert.Equal(ContentPageResult.VersionConflict, await logic.DeleteAsync(id, 2));
        Assert.Equal(ContentPageResult.Success, await logic.DeleteAsync(id, 3));
        Assert.Equal(ContentPageResult.NotFound, await logic.DeleteAsync(id, 3));
    }

    private sealed class StubPages : IContentPageOperations
    {
        public Dictionary<Guid, ContentPage> Items { get; } = [];

        public Task<IReadOnlyList<ContentPage>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContentPage>>(Items.Values.ToList());

        public Task<ContentPage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.GetValueOrDefault(id));

        public Task<PageWriteResult> SaveAsync(ContentPage page, bool isNew, CancellationToken cancellationToken = default)
        {
            if (Items.Values.Any(item => item.Id != page.Id && item.Slug == page.Slug))
                return Task.FromResult(PageWriteResult.SlugConflict);
            if (!isNew && !Items.ContainsKey(page.Id)) return Task.FromResult(PageWriteResult.NotFound);
            if (!isNew && Items[page.Id].Revision != page.Revision) return Task.FromResult(PageWriteResult.VersionConflict);
            Items[page.Id] = page with { Revision = isNew ? 1 : page.Revision + 1 };
            return Task.FromResult(PageWriteResult.Success);
        }

        public Task<PageWriteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken = default)
        {
            if (!Items.TryGetValue(id, out var item)) return Task.FromResult(PageWriteResult.NotFound);
            if (item.Revision != revision) return Task.FromResult(PageWriteResult.VersionConflict);
            Items.Remove(id);
            return Task.FromResult(PageWriteResult.Success);
        }

        public Task<Guid?> ResolvePublishedSlugAsync(string slug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
