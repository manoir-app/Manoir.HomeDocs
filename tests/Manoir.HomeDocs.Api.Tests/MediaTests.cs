using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class MediaSniffTests
{
    [Fact]
    public void RecognizesSupportedFormatsFromTheirSignatureOnly()
    {
        Assert.Equal(("image", "image/png"), MediaLogic.Sniff(MediaApiTests.Png));
        Assert.Equal(("image", "image/jpeg"), MediaLogic.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0x00]));
        Assert.Equal(("image", "image/gif"), MediaLogic.Sniff("GIF89a...."u8));
        Assert.Equal(("image", "image/webp"), MediaLogic.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal(("pdf", "application/pdf"), MediaLogic.Sniff(MediaApiTests.Pdf));
        Assert.Null(MediaLogic.Sniff("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8));
        Assert.Null(MediaLogic.Sniff("<html></html>"u8));
        Assert.Null(MediaLogic.Sniff([]));
    }

    [Fact]
    public void FindsEveryMediaReferenceOnce()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var markdown = $"![a](/api/public/media/{first}) texte [b](/api/public/media/{second.ToString().ToUpperInvariant()}) ![c](/api/public/media/{first})";
        Assert.Equal(new HashSet<Guid> { first, second }, MediaLogic.ReferencedMedia(markdown).ToHashSet());
        Assert.Empty(MediaLogic.ReferencedMedia("![x](https://exemple.test/api/public/media/pas-un-guid)"));
    }
}

public sealed class MediaApiTests : IDisposable
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    internal static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-media-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program> factory;

    public MediaApiTests() => factory = CreateFactory();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));

    [Fact]
    public async Task UploadValidatesContentAndRequiresAnAdministrator()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/media/")).StatusCode);
        using var anonymousUpload = await anonymous.PostAsync("/api/admin/media/", Upload("a.png", Png));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousUpload.StatusCode);

        using var admin = await SignedInAdminAsync();
        using var withoutToken = await admin.PostAsync("/api/admin/media/", Upload("a.png", Png));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        // The signature decides, not the name or the declared type: scripts and HTML are refused.
        using var svg = await SendAsync(admin, HttpMethod.Post, "/api/admin/media/",
            Upload("logo.png", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "image/png"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, svg.StatusCode);
        using var empty = await SendAsync(admin, HttpMethod.Post, "/api/admin/media/", Upload("vide.png", []));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var tooLarge = await SendAsync(admin, HttpMethod.Post, "/api/admin/media/",
            Upload("gros.pdf", [.. Pdf, .. new byte[MediaLogic.MaxBytes]]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Empty(await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/media/") ?? []);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "media")));
    }

    [Fact]
    public async Task LibraryImportsSearchesClassifiesAndRenames()
    {
        using var admin = await SignedInAdminAsync();
        var image = await UploadAsync(admin, "chemin\\Plan cuisine.PNG", Png, title: "Plan de la cuisine", alt: "Vue du dessus", folder: "Cuisine");
        var pdf = await UploadAsync(admin, "notice-four.pdf", Pdf, folder: "Notices");

        Assert.Equal("Plan cuisine.PNG", image.Media.Name);
        Assert.Equal("image", image.Media.Kind);
        Assert.Equal("image/png", image.Media.ContentType);
        Assert.Equal($"![Vue du dessus](/api/public/media/{image.Media.Id})", image.Media.Markdown);
        Assert.Equal("pdf", pdf.Media.Kind);
        Assert.Equal("notice-four", pdf.Media.Title);
        Assert.Equal($"[notice-four](/api/public/media/{pdf.Media.Id})", pdf.Media.Markdown);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "media"), "*.bin"), path => path.Contains(image.Media.Id.ToString("N")));

        Assert.Equal(2, (await ListAsync(admin, "")).Length);
        Assert.Equal(image.Media.Id, Assert.Single(await ListAsync(admin, "?q=PLAN%20vue")).Id);
        Assert.Equal(pdf.Media.Id, Assert.Single(await ListAsync(admin, "?folder=notices")).Id);
        Assert.Equal(pdf.Media.Id, Assert.Single(await ListAsync(admin, "?kind=pdf")).Id);
        Assert.Empty(await ListAsync(admin, "?q=inexistant"));

        using var rename = await SendAsync(admin, HttpMethod.Put, $"/api/admin/media/{pdf.Media.Id}",
            JsonContent.Create(new { revision = pdf.Media.Revision, name = "four.pdf", title = "Notice du four", altText = "", folder = "Cuisine" }));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var renamed = (await rename.Content.ReadFromJsonAsync<AdminMediaDetail>())!;
        Assert.Equal("four.pdf", renamed.Media.Name);
        Assert.Equal(pdf.Media.Revision + 1, renamed.Media.Revision);
        Assert.Contains("[Notice du four]", renamed.Media.Markdown);
        using var stale = await SendAsync(admin, HttpMethod.Put, $"/api/admin/media/{pdf.Media.Id}",
            JsonContent.Create(new { revision = pdf.Media.Revision, title = "Autre" }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var tooLong = await SendAsync(admin, HttpMethod.Put, $"/api/admin/media/{pdf.Media.Id}",
            JsonContent.Create(new { revision = renamed.Media.Revision, folder = new string('x', 65) }));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(2, (await ListAsync(admin, "?folder=cuisine")).Length);
    }

    [Fact]
    public async Task UsagesAreListedAndUsedMediaIsProtectedFromDeletion()
    {
        using var admin = await SignedInAdminAsync();
        var image = await UploadAsync(admin, "cuisine.png", Png, alt: "La cuisine");
        var unused = await UploadAsync(admin, "oubli.pdf", Pdf);
        var pageId = await CreateAndPublishAsync(admin, new ContentPageInput(
            "Cuisine", "cuisine", "piece", [], false, $"# Cuisine\n\n{image.Media.Markdown}\n"));

        var detail = (await admin.GetFromJsonAsync<AdminMediaDetail>($"/api/admin/media/{image.Media.Id}"))!;
        var usage = Assert.Single(detail.Usages);
        Assert.Equal(pageId, usage.PageId);
        Assert.Equal("cuisine", usage.Slug);
        Assert.Equal(1, Assert.Single(await ListAsync(admin, "?q=cuisine")).UsageCount);

        using var blocked = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/media/{image.Media.Id}?revision={image.Media.Revision}", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("\"slug\":\"cuisine\"", body);
        Assert.NotNull(await admin.GetFromJsonAsync<AdminMediaDetail>($"/api/admin/media/{image.Media.Id}"));

        using var removeUnused = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/media/{unused.Media.Id}?revision={unused.Media.Revision}", null);
        Assert.Equal(HttpStatusCode.NoContent, removeUnused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/media/{unused.Media.Id}")).StatusCode);
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(root, "media")), path => path.Contains(unused.Media.Id.ToString("N")));

        using var staleDelete = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/media/{image.Media.Id}?revision=99&force=true", null);
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using var forced = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/media/{image.Media.Id}?revision={image.Media.Revision}&force=true", null);
        Assert.Equal(HttpStatusCode.NoContent, forced.StatusCode);
        Assert.Empty(await ListAsync(admin, ""));
    }

    [Fact]
    public async Task PublicAccessFollowsThePagesThatUseTheMedia()
    {
        using var admin = await SignedInAdminAsync();
        await CreateContextAsync(admin, "invites");
        await CreateContextAsync(admin, "menage");
        using var setPin = await SendAsync(admin, HttpMethod.Put, "/api/admin/settings/pin",
            JsonContent.Create(new { currentPassword = "test password long enough", pin = "012345" }));
        Assert.Equal(HttpStatusCode.NoContent, setPin.StatusCode);
        var open = await UploadAsync(admin, "plan.png", Png, alt: "Plan");
        var secret = await UploadAsync(admin, "code.pdf", Pdf);
        var orphan = await UploadAsync(admin, "orphelin.png", Png);
        var draft = await UploadAsync(admin, "brouillon.png", Png);
        await CreateAndPublishAsync(admin, new ContentPageInput("Plan", "plan", "page", ["invites"], false, open.Media.Markdown));
        await CreateAndPublishAsync(admin, new ContentPageInput("Coffre", "coffre", "page", ["invites"], true, secret.Media.Markdown));
        using var draftPage = await SendAsync(admin, HttpMethod.Post, "/api/admin/pages/", JsonContent.Create(new
        {
            page = new ContentPageInput("Brouillon", "brouillon", "page", [], false, draft.Media.Markdown),
            placement = new { parentNodeId = (Guid?)null }
        }));
        Assert.Equal(HttpStatusCode.Created, draftPage.StatusCode);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string Url(Guid id, string context = "invites") => $"/api/public/media/{id}?context={context}&profile=desktop";

        using var served = await anonymous.GetAsync(Url(open.Media.Id));
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", Assert.Single(served.Headers.GetValues("X-Content-Type-Options")));
        Assert.StartsWith("inline", served.Content.Headers.ContentDisposition?.DispositionType);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(Url(open.Media.Id, "menage"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(Url(open.Media.Id, "inconnu"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/media/{open.Media.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(Url(orphan.Media.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(Url(draft.Media.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(secret.Media.Id))).StatusCode);
        using var authorize = await SendAsync(anonymous, HttpMethod.Post, "/api/public/access/pin",
            JsonContent.Create(new { context = "invites", profile = "desktop", pin = "012345" }));
        Assert.Equal(HttpStatusCode.NoContent, authorize.StatusCode);
        using var secretServed = await anonymous.GetAsync(Url(secret.Media.Id));
        Assert.Equal(HttpStatusCode.OK, secretServed.StatusCode);
        Assert.Equal("application/pdf", secretServed.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", secretServed.Headers.CacheControl?.ToString());

        // An administrator previews any media without choosing a context.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/public/media/{orphan.Media.Id}")).StatusCode);
    }

    [Fact]
    public async Task FilesAndMetadataSurviveARestart()
    {
        using var admin = await SignedInAdminAsync();
        var image = await UploadAsync(admin, "salon.png", Png, alt: "Salon", folder: "Pièces");
        await CreateAndPublishAsync(admin, new ContentPageInput("Salon", "salon", "piece", [], false, image.Media.Markdown));
        await CreateContextAsync(admin, "habitant");
        factory.Dispose();

        factory = CreateFactory();
        using var restarted = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var served = await restarted.GetAsync($"/api/public/media/{image.Media.Id}?context=habitant&profile=tv");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
    }

    private static MultipartFormDataContent Upload(string fileName, byte[] content, string contentType = "application/octet-stream",
        string? title = null, string? alt = null, string? folder = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        if (title is not null) form.Add(new StringContent(title), "title");
        if (alt is not null) form.Add(new StringContent(alt), "altText");
        if (folder is not null) form.Add(new StringContent(folder), "folder");
        return form;
    }

    private static async Task<AdminMediaDetail> UploadAsync(HttpClient client, string fileName, byte[] content,
        string? title = null, string? alt = null, string? folder = null)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/admin/media/", Upload(fileName, content, "application/octet-stream", title, alt, folder));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminMediaDetail>())!;
    }

    private static async Task<AdminMediaItem[]> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<AdminMediaItem[]>("/api/admin/media/" + query))!;

    private async Task<HttpClient> SignedInAdminAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var setup = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/initialize",
            JsonContent.Create(new { userName = "admin", password = "test password long enough" }));
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        using var login = await SendAsync(client, HttpMethod.Post, "/api/admin/auth/login",
            JsonContent.Create(new { userName = "admin", password = "test password long enough" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task CreateContextAsync(HttpClient client, string id)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/admin/contexts/",
            JsonContent.Create(new { id, name = id, rootNodeId = (Guid?)null }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> CreateAndPublishAsync(HttpClient client, ContentPageInput page)
    {
        using var create = await SendAsync(client, HttpMethod.Post, "/api/admin/pages/",
            JsonContent.Create(new { page, placement = new { parentNodeId = (Guid?)null } }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved = await client.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        using var publish = await SendAsync(client, HttpMethod.Post, $"/api/admin/pages/{id}/publication",
            JsonContent.Create(new { revision = saved!.Revision, isPublished = true }));
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        return id;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, HttpContent? content)
    {
        using var tokenResponse = await client.GetAsync("/api/admin/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(method, path) { Content = content };
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
