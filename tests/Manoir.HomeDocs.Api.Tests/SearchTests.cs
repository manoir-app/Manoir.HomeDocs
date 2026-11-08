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

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Électricité  ÇA-va!", "electricite ca va")]
    [InlineData("  ", "")]
    [InlineData("Œuf", "ouf")]
    public void NormalizeRemovesCaseAccentsAndPunctuation(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void PlainTextAndSummarySkipMarkup()
    {
        const string markdown = "# Titre\n\nPremier **paragraphe** avec un [lien](https://exemple.test) et `code`.\n\n```\nignoré\n```\n\n- liste";
        var plain = SearchText.ToPlainText(markdown);
        Assert.Contains("Premier paragraphe avec un lien et code.", plain);
        Assert.DoesNotContain("ignoré", plain);
        Assert.DoesNotContain("https", plain);
        Assert.Equal("Premier paragraphe avec un lien et code.", SearchText.BuildSummary(markdown));
    }

    [Theory]
    [InlineData("frappes", "phrappes")]
    [InlineData("fautes", "photes")]
    [InlineData("essuie", "essui")]
    public void FoldBringsPhoneticSpellingsWithinOneEdit(string word, string misspelled) =>
        Assert.True(SearchText.Distance(SearchText.Fold(word), SearchText.Fold(misspelled)) <= 1);

    [Fact]
    public void DistanceCountsTranspositionsAsOneEdit() =>
        Assert.Equal(1, SearchText.Distance("cuisine", "cuisnie"));
}

public sealed class SearchApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-search-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public SearchApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));
    }

    [Fact]
    public async Task SearchFindsPagesOfTheContextWithSynonymsAndTypos()
    {
        using var admin = await SignedInAdminAsync();
        await CreateContextAsync(admin, "invites", "Invités");
        await CreateContextAsync(admin, "menage", "Ménage");
        var rootId = await CreateAndPublishAsync(admin,
            new ContentPageInput("Accueil invités", "accueil-invites", "page", ["invites"], false, "# Accueil\n\nBienvenue chez nous."), null);
        var rootNodeId = await NodeIdOfAsync(admin, rootId);
        await CreateAndPublishAsync(admin,
            new ContentPageInput("Essuie-tout", "essuie-tout", "emplacement", ["invites"], false,
                "# Essuie-tout\n\nLes rouleaux sont dans le placard sous l'évier."), rootNodeId);
        await CreateAndPublishAsync(admin,
            new ContentPageInput("Se connecter à Internet", "internet", "procedure", [], false,
                "# Internet\n\nChoisir le réseau de la maison puis saisir le mot de passe."), rootNodeId);
        await CreateAndPublishAsync(admin,
            new ContentPageInput("Produits ménagers", "produits", "emplacement", ["menage"], false,
                "# Produits\n\nLe vinaigre blanc est sous l'évier du ménage."), null);
        await CreateAndPublishAsync(admin,
            new ContentPageInput("Brouillon", "brouillon", "page", [], false, "# Brouillon\n\nvinaigre brouillon"), null,
            publish: false);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/public/search?q=wifi&context=inconnu&profile=desktop")).StatusCode);

        var bySynonym = await SearchAsync(anonymous, "sopalin");
        var essuie = Assert.Single(bySynonym.Results);
        Assert.Equal("essuie-tout", essuie.Slug);
        Assert.Equal("emplacement", essuie.Category);
        Assert.Contains("évier", essuie.Excerpt);
        Assert.Equal(["Accueil invités"], essuie.Path);

        Assert.Equal("internet", Assert.Single((await SearchAsync(anonymous, "wifi")).Results).Slug);
        Assert.Equal("internet", Assert.Single((await SearchAsync(anonymous, "rese")).Results).Slug);
        Assert.Equal("essuie-tout", Assert.Single((await SearchAsync(anonymous, "ROULEAUX placard")).Results).Slug);

        // Pages of another context and unpublished pages never appear.
        var vinegar = await SearchAsync(anonymous, "vinaigre");
        Assert.Empty(vinegar.Results);
        var menage = await SearchAsync(anonymous, "vinaigre", "menage");
        Assert.Equal("produits", Assert.Single(menage.Results).Slug);

        // Typos, accents and French phonetic spellings are corrected against the indexed words.
        var typo = await SearchAsync(anonymous, "rouleaus");
        Assert.True(typo.Corrected);
        Assert.Equal("essuie-tout", Assert.Single(typo.Results).Slug);
        var phonetic = await SearchAsync(anonymous, "plackard");
        Assert.Equal("essuie-tout", Assert.Single(phonetic.Results).Slug);
        Assert.Empty((await SearchAsync(anonymous, "zzzzzz")).Results);
        Assert.Empty((await SearchAsync(anonymous, "   ")).Results);
        Assert.Empty((await SearchAsync(anonymous, "a\"b* OR NOT ( )")).Results);
    }

    [Fact]
    public async Task ProtectedPagesExposeTitleAndSummaryButNeverTheBodyBeforePin()
    {
        using var admin = await SignedInAdminAsync();
        await CreateContextAsync(admin, "invites", "Invités");
        using var setPin = await SendAsync(admin, HttpMethod.Put, "/api/admin/settings/pin",
            new { currentPassword = "test password long enough", pin = "012345" });
        Assert.Equal(HttpStatusCode.NoContent, setPin.StatusCode);
        await CreateAndPublishAsync(admin,
            new ContentPageInput("Coffre de la chambre", "coffre", "equipement", ["invites"], true,
                "Le coffre contient les papiers.\n\nLe code secret est zorglub."), null);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var byTitle = await SearchAsync(anonymous, "coffre");
        var locked = Assert.Single(byTitle.Results);
        Assert.True(locked.RequiresAuthentication);
        Assert.Equal("Le coffre contient les papiers.", locked.Summary);
        Assert.Null(locked.Excerpt);

        // A word that only occurs in the protected body must not even reveal that the page exists.
        Assert.Empty((await SearchAsync(anonymous, "zorglub")).Results);
        Assert.Empty((await SearchAsync(anonymous, "zorgleb")).Results);
        var raw = await anonymous.GetStringAsync("/api/public/search?q=coffre&context=invites&profile=desktop");
        Assert.DoesNotContain("zorglub", raw, StringComparison.Ordinal);

        using var authorize = await SendAsync(anonymous, HttpMethod.Post, "/api/public/access/pin",
            new { context = "invites", profile = "desktop", pin = "012345" });
        Assert.Equal(HttpStatusCode.NoContent, authorize.StatusCode);
        var unlocked = Assert.Single((await SearchAsync(anonymous, "zorglub")).Results);
        Assert.Equal("coffre", unlocked.Slug);
        Assert.Contains("zorglub", unlocked.Excerpt);
    }

    [Fact]
    public async Task IndexFollowsEditsPublicationAndDeletionAndSurvivesRestart()
    {
        using var admin = await SignedInAdminAsync();
        await CreateContextAsync(admin, "invites", "Invités");
        var id = await CreateAndPublishAsync(admin,
            new ContentPageInput("Aspirateur", "aspirateur", "equipement", [], false, "# Aspirateur\n\nRangé dans le cellier."), null);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Single((await SearchAsync(anonymous, "cellier")).Results);

        var page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        using var edit = await SendAsync(admin, HttpMethod.Put, $"/api/admin/pages/{id}", new
        {
            revision = page!.Revision,
            page = new ContentPageInput(page.Title, page.Slug, page.Category, [], false, "# Aspirateur\n\nRangé dans le garage.")
        });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        Assert.Empty((await SearchAsync(anonymous, "cellier")).Results);
        Assert.Single((await SearchAsync(anonymous, "garage")).Results);

        page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        using var unpublish = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/publication",
            new { revision = page!.Revision, isPublished = false });
        Assert.Equal(HttpStatusCode.NoContent, unpublish.StatusCode);
        Assert.Empty((await SearchAsync(anonymous, "garage")).Results);

        page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        using var publish = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/publication",
            new { revision = page!.Revision, isPublished = true });
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        Assert.Single((await SearchAsync(anonymous, "garage")).Results);

        // The index is derived data: a new host rebuilds it from the stored pages.
        using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));
        using var restartedClient = restarted.CreateClient();
        var afterRestart = await restartedClient.GetFromJsonAsync<PublicSearchResponse>(
            "/api/public/search?q=garage&context=invites&profile=desktop");
        Assert.Single(afterRestart!.Results);

        page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        using var delete = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/pages/{id}?revision={page!.Revision}", null);
        Assert.True(delete.IsSuccessStatusCode);
        Assert.Empty((await SearchAsync(anonymous, "garage")).Results);
    }

    private async Task<PublicSearchResponse> SearchAsync(HttpClient client, string query, string context = "invites")
    {
        using var response = await client.GetAsync(
            $"/api/public/search?q={Uri.EscapeDataString(query)}&context={context}&profile=desktop");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<PublicSearchResponse>())!;
    }

    private async Task<HttpClient> SignedInAdminAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var setup = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/initialize",
            new { userName = "admin", password = "test password long enough" });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        using var login = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/login",
            new { userName = "admin", password = "test password long enough" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task CreateContextAsync(HttpClient client, string id, string name)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/admin/contexts/",
            new { id, name, rootNodeId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> NodeIdOfAsync(HttpClient client, Guid pageId)
    {
        var tree = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/tree/");
        return Assert.Single(tree!, item => item.GetProperty("pageId").GetGuid() == pageId).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAndPublishAsync(HttpClient client, ContentPageInput page, Guid? parentNodeId, bool publish = true)
    {
        using var create = await SendAsync(client, HttpMethod.Post, "/api/admin/pages/",
            new { page, placement = new { parentNodeId } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (!publish) return id;
        var saved = await client.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        using var published = await SendAsync(client, HttpMethod.Post, $"/api/admin/pages/{id}/publication",
            new { revision = saved!.Revision, isPublished = true });
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        return id;
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
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "database", "homedocs.db")
        }.ToString());
        SqliteConnection.ClearPool(connection);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
