using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class ContentPageApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-pages-api-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public ContentPageApiTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));
    }

    [Fact]
    public async Task AdministratorManagesPagesAndAnonymousRequestsCannotReadOrWrite()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var input = new ContentPageInput("Cuisine", "cuisine", "piece", ["invites"], true, "# Texte privé");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/pages/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/pages/", input)).StatusCode);
        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync($"/api/admin/pages/{unknown}", new { revision = 1, page = input })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.DeleteAsync($"/api/admin/pages/{unknown}?revision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync($"/api/admin/pages/{unknown}/publication", new { revision = 1, isPublished = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync($"/api/admin/pages/{unknown}/duplicate", new { title = "Copie", slug = "copie" })).StatusCode);

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var authToken = await TokenAsync(admin);
        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/initialize")
        {
            Content = JsonContent.Create(new { userName = "admin", password = "test password long enough" })
        })
        {
            setup.Headers.Add("X-CSRF-TOKEN", authToken);
            Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(setup)).StatusCode);
        }
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/login")
        {
            Content = JsonContent.Create(new { userName = "admin", password = "test password long enough" })
        })
        {
            login.Headers.Add("X-CSRF-TOKEN", await TokenAsync(admin));
            Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(login)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/pages/", input)).StatusCode);
        using var created = await SendAsync(admin, HttpMethod.Post, "/api/admin/pages/", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        Assert.NotNull(page);
        Assert.Equal(input.Markdown, page.Markdown);
        Assert.False(page.IsPublished);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/admin/pages/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/admin/pages/{id}/publication", new { revision = page.Revision, isPublished = true })).StatusCode);
        using var incompleteProtection = await SendAsync(admin, HttpMethod.Put, $"/api/admin/pages/{id}",
            new { revision = page.Revision, page = input with { RequiresAuthentication = null } });
        Assert.Equal(HttpStatusCode.BadRequest, incompleteProtection.StatusCode);
        using var incompletePublication = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/publication", new { revision = page.Revision });
        Assert.Equal(HttpStatusCode.BadRequest, incompletePublication.StatusCode);

        using var duplicateSlug = await SendAsync(admin, HttpMethod.Post, "/api/admin/pages/", input);
        Assert.Equal(HttpStatusCode.Conflict, duplicateSlug.StatusCode);
        using var published = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/publication", new { revision = page.Revision, isPublished = true });
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        using var stale = await SendAsync(admin, HttpMethod.Put, $"/api/admin/pages/{id}", new { revision = page.Revision, page = input });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        Assert.NotNull(page);
        using var update = await SendAsync(admin, HttpMethod.Put, $"/api/admin/pages/{id}",
            new { revision = page.Revision, page = input with { Slug = "ma-cuisine", Markdown = "# Nouvelle version" } });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        page = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{id}");
        Assert.NotNull(page);
        Assert.True(page.IsPublished);
        Assert.Equal("# Nouvelle version", page.Markdown);

        using var duplicate = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{id}/duplicate", new { title = "Copie", slug = "copie" });
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        var copyId = (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var copy = await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{copyId}");
        Assert.NotNull(copy);
        Assert.False(copy.IsPublished);
        Assert.True(copy.RequiresAuthentication);
        using var publicResponse = await anonymous.GetAsync("/ma-cuisine");
        Assert.DoesNotContain("Nouvelle version", await publicResponse.Content.ReadAsStringAsync());
        using var deleted = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/pages/{id}?revision={page.Revision}", null);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/pages/{id}")).StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", await TokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/admin/auth/antiforgery");
        return token.GetProperty("token").GetString()!;
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
