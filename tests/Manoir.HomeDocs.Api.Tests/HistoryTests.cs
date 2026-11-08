using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class MarkdownLinksTests
{
    [Fact]
    public void ExtractsPageMediaAndExternalLinksOnly()
    {
        var media = Guid.NewGuid();
        var markdown = $"""
            [a](/cuisine) [b](cuisine?x=1#haut) [c](/api/public/media/{media}) ![d](/api/public/media/{media})
            [e](https://exemple.test/x) [f](#ancre) [g](/a/b/c) [h](/Majuscule) [i](</salon>)
            `[code](/ignore)`

            ```
            [bloc](/ignore-aussi)
            ```
            """;
        var links = MarkdownLinks.Extract(markdown);

        Assert.Equal(["cuisine", "salon"], links.Where(link => link.Kind == LinkTargetKind.Page).Select(link => link.Slug).Distinct().Order());
        Assert.Equal(media, Assert.Single(links, link => link.Kind == LinkTargetKind.Media).MediaId);
        Assert.Equal("https://exemple.test/x", Assert.Single(links, link => link.Kind == LinkTargetKind.External).Href);
        Assert.DoesNotContain(links, link => link.Href.Contains("ignore", StringComparison.Ordinal));
    }
}

public sealed class HistoryApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-history-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program> factory;

    public HistoryApiTests() => factory = CreateFactory();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));

    [Fact]
    public async Task EveryWriteRecordsAVersionAndAnyVersionCanBeRestoredWithoutBreakingTheSlug()
    {
        using var admin = await SignedInAdminAsync();
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/admin/pages/{Guid.NewGuid()}/versions")).StatusCode);

        var id = await CreatePageAsync(admin, new ContentPageInput("Four", "four", "equipement", [], false, "# Four\n\nVersion un."));
        await UpdateAsync(admin, id, new ContentPageInput("Four", "four", "equipement", [], false, "# Four\n\nVersion deux."));
        await SetPublicationAsync(admin, id, true);
        await UpdateAsync(admin, id, new ContentPageInput("Four à pyrolyse", "four-pyrolyse", "equipement", [], true, "# Four\n\nVersion trois."));
        await SetPublicationAsync(admin, id, false);

        var versions = await VersionsAsync(admin, id);
        Assert.Equal([5L, 4L, 3L, 2L, 1L], versions.Select(version => version.Revision));
        Assert.Equal(["unpublished", "edited", "published", "edited", "created"], versions.Select(version => version.Event));
        Assert.Equal(["four-pyrolyse", "four-pyrolyse", "four", "four", "four"], versions.Select(version => version.Slug));
        Assert.True(versions[1].RequiresAuthentication);

        var first = (await admin.GetFromJsonAsync<ContentVersion>($"/api/admin/pages/{id}/versions/1"))!;
        Assert.Contains("Version un.", first.Markdown);
        Assert.Equal("equipement", first.Category);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/pages/{id}/versions/99")).StatusCode);

        // The current page is a draft titled "Four à pyrolyse" with the slug "four-pyrolyse".
        using var stale = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/versions/1/restore", new { revision = 2 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var unknown = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/versions/99/restore", new { revision = 5 });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var restore = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/versions/1/restore", new { revision = 5 });
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        var page = (await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}"))!;
        Assert.Equal(6, page.Revision);
        Assert.Equal("Four", page.Title);
        Assert.Contains("Version un.", page.Markdown);
        Assert.False(page.RequiresAuthentication);
        Assert.Equal("four-pyrolyse", page.Slug);
        Assert.False(page.IsPublished);

        var after = await VersionsAsync(admin, id);
        Assert.Equal(6, after.Count);
        Assert.Equal("restored", after[0].Event);
        Assert.Equal(1, after[0].RestoredFromRevision);
        Assert.Equal(first.Summary.Size, after[0].Size);

        using var delete = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/pages/{id}?revision=6", null);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/pages/{id}/versions")).StatusCode);
    }

    [Fact]
    public async Task PagesWithoutHistoryGetABaselineAtStartup()
    {
        using var admin = await SignedInAdminAsync();
        var id = await CreatePageAsync(admin, new ContentPageInput("Ancienne", "ancienne", "page", [], false, "# Ancienne"));
        factory.Dispose();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "database", "homedocs.db")
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM \"ContentPageVersions\"";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        factory = CreateFactory();
        using var restarted = await SignedInAgainAsync();
        var version = Assert.Single(await VersionsAsync(restarted, id));
        Assert.Equal("baseline", version.Event);
        Assert.Equal(1, version.Revision);
    }

    [Fact]
    public async Task LinksReportStatusesIncomingLinksAndBrokenReferences()
    {
        using var admin = await SignedInAdminAsync();
        var cuisine = await CreatePageAsync(admin, new ContentPageInput("Cuisine", "cuisine", "piece", [], false, "# Cuisine"), publish: true);
        var brouillon = await CreatePageAsync(admin, new ContentPageInput("Brouillon", "brouillon", "page", [], false, "# Brouillon"));
        var ancienne = await CreatePageAsync(admin, new ContentPageInput("Ancien nom", "ancien-nom", "page", [], false, "# Ancien"), publish: true);
        await UpdateAsync(admin, ancienne, new ContentPageInput("Nouveau nom", "nouveau-nom", "page", [], false, "# Nouveau"));
        var missingMedia = Guid.NewGuid();
        var accueil = await CreatePageAsync(admin, new ContentPageInput("Accueil", "accueil", "page", [], false,
            $"[ok](/cuisine) [draft](/brouillon) [old](/ancien-nom) [gone](/disparue) [web](https://exemple.test) ![m](/api/public/media/{missingMedia})"));

        var report = (await admin.GetFromJsonAsync<PageLinkReport>($"/api/admin/pages/{accueil}/links"))!;
        string StatusOf(string href) => report.Outgoing.Single(link => link.Href == href).Status;
        Assert.Equal("ok", StatusOf("/cuisine"));
        Assert.Equal("draft", StatusOf("/brouillon"));
        Assert.Equal("redirected", StatusOf("/ancien-nom"));
        Assert.Equal(ancienne, report.Outgoing.Single(link => link.Href == "/ancien-nom").TargetId);
        Assert.Equal("broken", StatusOf("/disparue"));
        Assert.Equal("unchecked", StatusOf("https://exemple.test"));
        Assert.Equal("broken", StatusOf($"/api/public/media/{missingMedia}"));

        var incoming = (await admin.GetFromJsonAsync<PageLinkReport>($"/api/admin/pages/{cuisine}/links"))!.Incoming;
        Assert.Equal(accueil, Assert.Single(incoming).PageId);
        Assert.Equal(accueil, Assert.Single((await admin.GetFromJsonAsync<PageLinkReport>($"/api/admin/pages/{brouillon}/links"))!.Incoming).PageId);

        var broken = (await admin.GetFromJsonAsync<BrokenLink[]>("/api/admin/links/broken"))!;
        Assert.Equal(2, broken.Length);
        Assert.All(broken, link => Assert.Equal(accueil, link.PageId));
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/links/broken")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/pages/{Guid.NewGuid()}/links")).StatusCode);
    }

    private static async Task<List<ContentVersionSummary>> VersionsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<ContentVersionSummary>>($"/api/admin/pages/{id}/versions"))!;

    private static async Task<Guid> CreatePageAsync(HttpClient client, ContentPageInput page, bool publish = false)
    {
        using var create = await SendAsync(client, HttpMethod.Post, "/api/admin/pages/",
            new { page, placement = new { parentNodeId = (Guid?)null } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (publish) await SetPublicationAsync(client, id, true);
        return id;
    }

    private static async Task UpdateAsync(HttpClient client, Guid id, ContentPageInput page)
    {
        var current = (await client.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}"))!;
        using var response = await SendAsync(client, HttpMethod.Put, $"/api/admin/pages/{id}", new { revision = current.Revision, page });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task SetPublicationAsync(HttpClient client, Guid id, bool published)
    {
        var current = (await client.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}"))!;
        using var response = await SendAsync(client, HttpMethod.Post, $"/api/admin/pages/{id}/publication",
            new { revision = current.Revision, isPublished = published });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpClient> SignedInAdminAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var setup = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/initialize",
            new { userName = "admin", password = "test password long enough" });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        await LoginAsync(client);
        return client;
    }

    private async Task<HttpClient> SignedInAgainAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAsync(client);
        return client;
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var login = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/login",
            new { userName = "admin", password = "test password long enough" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        using var tokenResponse = await client.GetAsync("/api/admin/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", json.RootElement.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }

    public void Dispose()
    {
        factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
