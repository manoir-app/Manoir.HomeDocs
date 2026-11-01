using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class UsageContextApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-contexts-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public UsageContextApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));
    }

    [Fact]
    public async Task ContextsControlPublicNavigationAndNeverExposeProtectedPages()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/contexts/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/public/navigation?context=invites&profile=desktop")).StatusCode);

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await InitializeAndSignInAsync(admin);
        Assert.Empty(await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/contexts/") ?? []);
        using var noCsrf = await admin.PostAsJsonAsync("/api/admin/contexts/", new { id = "invites", name = "Invités" });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        using var inviteContext = await SendAsync(admin, HttpMethod.Post, "/api/admin/contexts/",
            new { id = "invites", name = "Invités", rootNodeId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, inviteContext.StatusCode);
        using var houseworkContext = await SendAsync(admin, HttpMethod.Post, "/api/admin/contexts/",
            new { id = "menage", name = "Ménage", rootNodeId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, houseworkContext.StatusCode);
        using var rootlessContext = await SendAsync(admin, HttpMethod.Post, "/api/admin/contexts/",
            new { id = "habitant", name = "Habitant", rootNodeId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, rootlessContext.StatusCode);
        using var setPin = await SendAsync(admin, HttpMethod.Put, "/api/admin/settings/pin",
            new { currentPassword = "test password long enough", pin = "012345" });
        Assert.Equal(HttpStatusCode.NoContent, setPin.StatusCode);

        var rootPageId = await CreateAndPublishAsync(admin,
            new ContentPageInput("Accueil invités", "accueil-invites", "page", ["invites"], false, "# Accueil"), null);
        var rootTree = await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/tree/");
        var rootNode = Assert.Single(rootTree!, item => item.GetProperty("pageId").GetGuid() == rootPageId);
        var rootNodeId = rootNode.GetProperty("id").GetGuid();
        using var setRoot = await SendAsync(admin, HttpMethod.Put, "/api/admin/contexts/invites",
            new { id = "invites", name = "Invités", rootNodeId });
        Assert.Equal(HttpStatusCode.NoContent, setRoot.StatusCode);
        var rootPage = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{rootPageId}");
        Assert.NotNull(rootPage);
        using var rootContextChange = await SendAsync(admin, HttpMethod.Put, $"/api/admin/pages/{rootPageId}",
            new
            {
                revision = rootPage.Revision,
                page = new ContentPageInput(rootPage.Title, rootPage.Slug, rootPage.Category, ["menage"], false, rootPage.Markdown)
            });
        Assert.Equal(HttpStatusCode.Conflict, rootContextChange.StatusCode);

        var directPageId = await CreateAndPublishAsync(admin,
            new ContentPageInput("Page hors racine", "hors-racine", "page", ["invites"], false, "# Accès direct"), null);
        var hiddenPageId = await CreateAndPublishAsync(admin,
            new ContentPageInput("Page ménage", "page-menage", "page", ["menage"], false, "# Ménage"), null);
        var protectedPageId = await CreateAndPublishAsync(admin,
            new ContentPageInput("Page protégée", "page-protegee", "page", ["invites"], true, "# Secret"), rootNodeId);
        _ = await CreateAndPublishAsync(admin,
            new ContentPageInput("Universelle", "universelle", "page", [], false, "# Tout le monde"), null);

        using var invalidContextPage = await SendAsync(admin, HttpMethod.Post, "/api/admin/pages/",
            new
            {
                page = new ContentPageInput("Invalide", "contexte-invalide", "page", ["inconnu"], false, "# Non"),
                placement = new { parentNodeId = (Guid?)null }
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidContextPage.StatusCode);

        using var addHiddenLink = await SendAsync(admin, HttpMethod.Post, "/api/admin/tree/links",
            new { pageId = hiddenPageId, parentNodeId = rootNodeId });
        Assert.Equal(HttpStatusCode.NoContent, addHiddenLink.StatusCode);
        using var navigationResponse = await anonymous.GetAsync("/api/public/navigation?context=invites&profile=desktop");
        Assert.Equal(HttpStatusCode.OK, navigationResponse.StatusCode);
        var navigation = await navigationResponse.Content.ReadAsStringAsync();
        Assert.Contains("accueil-invites", navigation);
        Assert.DoesNotContain("page-menage", navigation);
        Assert.Contains("page-protegee", navigation);
        Assert.DoesNotContain("Secret", navigation);

        using var missingProfile = await anonymous.GetAsync("/api/public/pages/hors-racine?context=invites");
        Assert.Equal(HttpStatusCode.NotFound, missingProfile.StatusCode);
        using var unknownProfile = await anonymous.GetAsync("/api/public/pages/hors-racine?context=invites&profile=unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknownProfile.StatusCode);
        using var direct = await anonymous.GetAsync("/api/public/pages/hors-racine?context=invites&profile=mobile&device=unknown-device");
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Contains("Accès direct", await direct.Content.ReadAsStringAsync());
        using var unavailable = await anonymous.GetAsync("/api/public/pages/page-menage?context=invites&profile=tablet-wall&device=wall-tablet");
        Assert.Equal(HttpStatusCode.Redirect, unavailable.StatusCode);
        Assert.Equal("/api/public/pages/accueil-invites?context=invites&profile=tablet-wall&device=wall-tablet", unavailable.Headers.Location?.OriginalString);
        using var protectedResponse = await anonymous.GetAsync("/api/public/pages/page-protegee?context=invites&profile=desktop");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        Assert.DoesNotContain("Secret", await protectedResponse.Content.ReadAsStringAsync());
        using var wrongPin = await SendAsync(anonymous, HttpMethod.Post, "/api/public/access/pin",
            new { context = "invites", profile = "desktop", pin = "111111" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPin.StatusCode);
        using var authorize = await SendAsync(anonymous, HttpMethod.Post, "/api/public/access/pin",
            new { context = "invites", profile = "desktop", device = "kitchen-tablet", pin = "012345" });
        Assert.Equal(HttpStatusCode.NoContent, authorize.StatusCode);
        var pinCookie = Assert.Single(authorize.Headers.GetValues("Set-Cookie"));
        Assert.Contains("HomeDocs.PublicPin", pinCookie);
        Assert.Contains("httponly", pinCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("012345", pinCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("012345", await authorize.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var accessStatus = await anonymous.GetAsync("/api/public/access/status?context=invites&profile=desktop");
        Assert.Equal(HttpStatusCode.NoContent, accessStatus.StatusCode);
        Assert.Contains(accessStatus.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains("HomeDocs.PublicPin", StringComparison.Ordinal));
        using var protectedPage = await anonymous.GetAsync("/api/public/pages/page-protegee?context=invites&profile=tv&device=another-device");
        Assert.Equal(HttpStatusCode.OK, protectedPage.StatusCode);
        Assert.Contains("Secret", await protectedPage.Content.ReadAsStringAsync());

        using var unknownContext = await anonymous.GetAsync("/api/public/pages/universelle?context=inconnu&profile=desktop");
        Assert.Equal(HttpStatusCode.NotFound, unknownContext.StatusCode);
        using var rootlessDirect = await anonymous.GetAsync("/api/public/pages/universelle?context=habitant&profile=desktop");
        Assert.Equal(HttpStatusCode.OK, rootlessDirect.StatusCode);
        using var rootlessNavigation = await anonymous.GetAsync("/api/public/navigation?context=habitant&profile=desktop");
        Assert.Equal(HttpStatusCode.NotFound, rootlessNavigation.StatusCode);
        using var contextSwitch = await anonymous.GetAsync("/api/public/navigation?context=habitant&profile=desktop");
        Assert.Equal(HttpStatusCode.NotFound, contextSwitch.StatusCode);
        using var expiredByContext = await anonymous.GetAsync("/api/public/pages/page-protegee?context=invites&profile=desktop");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredByContext.StatusCode);

        using var contextInUse = await SendAsync(admin, HttpMethod.Delete, "/api/admin/contexts/invites", null);
        Assert.Equal(HttpStatusCode.Conflict, contextInUse.StatusCode);
        using var invalidRoot = await SendAsync(admin, HttpMethod.Put, "/api/admin/contexts/invites",
            new { id = "invites", name = "Invités", rootNodeId = Guid.NewGuid() });
        Assert.Equal((HttpStatusCode)422, invalidRoot.StatusCode);
        Assert.NotEqual(Guid.Empty, directPageId);
        Assert.NotEqual(Guid.Empty, protectedPageId);
    }

    [Fact]
    public async Task PublicPinAttemptsAreRateLimited()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await InitializeAndSignInAsync(admin);
        using var context = await SendAsync(admin, HttpMethod.Post, "/api/admin/contexts/",
            new { id = "invites", name = "Invités", rootNodeId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, context.StatusCode);
        using var setPin = await SendAsync(admin, HttpMethod.Put, "/api/admin/settings/pin",
            new { currentPassword = "test password long enough", pin = "012345" });
        Assert.Equal(HttpStatusCode.NoContent, setPin.StatusCode);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var rejected = await SendAsync(anonymous, HttpMethod.Post, "/api/public/access/pin",
                new { context = "invites", profile = "desktop", pin = "111111" });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.DoesNotContain("111111", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using var limited = await SendAsync(anonymous, HttpMethod.Post, "/api/public/access/pin",
            new { context = "invites", profile = "desktop", pin = "111111" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task DemoContentIsInstalledOnlyOnRequestAndCanBeSafelyRetried()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var anonymousInstall = await anonymous.PostAsync("/api/admin/demo/install", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousInstall.StatusCode);

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await InitializeAndSignInAsync(admin);
        Assert.Empty(await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/contexts/") ?? []);
        Assert.Empty(await admin.GetFromJsonAsync<ContentPage[]>("/api/admin/pages/") ?? []);

        using var noCsrf = await admin.PostAsync("/api/admin/demo/install", null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        using var install = await SendAsync(admin, HttpMethod.Post, "/api/admin/demo/install", null);
        Assert.Equal(HttpStatusCode.OK, install.StatusCode);
        using var installResult = JsonDocument.Parse(await install.Content.ReadAsStringAsync());
        Assert.Equal("installed", installResult.RootElement.GetProperty("status").GetString());

        var contexts = await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/contexts/");
        Assert.Equal(3, contexts?.Length);
        Assert.All(contexts!, context => Assert.NotEqual(Guid.Empty, context.GetProperty("rootNodeId").GetGuid()));

        var pages = await admin.GetFromJsonAsync<ContentPage[]>("/api/admin/pages/");
        Assert.NotNull(pages);
        Assert.True(pages.Length >= 40);
        Assert.All(pages, page =>
        {
            Assert.True(page.IsPublished);
            Assert.Contains("Contenu fictif de démonstration.", page.Markdown, StringComparison.Ordinal);
        });

        using var navigationResponse = await anonymous.GetAsync("/api/public/navigation?context=menage&profile=tablet-wall&device=tablette-cuisine");
        Assert.Equal(HttpStatusCode.OK, navigationResponse.StatusCode);
        var navigation = await navigationResponse.Content.ReadAsStringAsync();
        Assert.Contains("instructions-menage", navigation);
        Assert.Contains("sortir-poubelles", navigation);
        Assert.Contains("tablette-cuisine", navigation);

        using var repeatedInstall = await SendAsync(admin, HttpMethod.Post, "/api/admin/demo/install", null);
        Assert.Equal(HttpStatusCode.OK, repeatedInstall.StatusCode);
        using var repeatedResult = JsonDocument.Parse(await repeatedInstall.Content.ReadAsStringAsync());
        Assert.Equal("alreadyInstalled", repeatedResult.RootElement.GetProperty("status").GetString());
    }

    private async Task<Guid> CreateAndPublishAsync(HttpClient client, ContentPageInput page, Guid? parentNodeId)
    {
        using var create = await SendAsync(client, HttpMethod.Post, "/api/admin/pages/",
            new { page, placement = new { parentNodeId } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved = await client.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        Assert.NotNull(saved);
        using var publish = await SendAsync(client, HttpMethod.Post, $"/api/admin/pages/{id}/publication",
            new { revision = saved.Revision, isPublished = true });
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        return id;
    }

    private async Task InitializeAndSignInAsync(HttpClient client)
    {
        using var setup = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/initialize",
            new { userName = "admin", password = "test password long enough" });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        using var login = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/login",
            new { userName = "admin", password = "test password long enough" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        using var tokenResponse = await client.GetAsync("/api/admin/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", token);
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
