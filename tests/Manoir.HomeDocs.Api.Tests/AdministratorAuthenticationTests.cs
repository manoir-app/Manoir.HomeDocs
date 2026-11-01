using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class AdministratorAuthenticationTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-auth-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public AdministratorAuthenticationTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "admin"));
        File.WriteAllText(Path.Combine(root, "admin", "index.html"), "admin interface");
        factory = CreateFactory();
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["HomeDocs:DataPath"] = Path.Combine(root, "data"),
                    ["Frontend:AdminRoot"] = Path.Combine(root, "admin")
                })));

    [Fact]
    public async Task InitializationLoginAndLogoutEnforceAuthorizationAndAntiforgery()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.False(await IsInitializedAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/other")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin/")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/admin/auth/initialize", new { userName = "admin", password = Password })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "initialize", "admin", "short")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, "initialize", "admin", Password)).StatusCode);
        Assert.True(await IsInitializedAsync(client));
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, "initialize", "other", Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, "login", "admin", "wrong password long enough")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, "login", "missing", Password)).StatusCode);
        using var login = await PostAsync(client, "login", "ADMIN", Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        using var session = await client.GetAsync("/api/admin/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var sessionBody = await session.Content.ReadAsStringAsync();
        Assert.Contains("admin", sessionBody);
        Assert.DoesNotContain(Password, sessionBody);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/private-page")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/admin/other")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/admin/auth/logout", null)).StatusCode);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "login", "admin", Password)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentInitializationAllowsOnlyOneAccount()
    {
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        var requests = Enumerable.Range(0, 8).Select(async index =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/auth/initialize")
            {
                Content = JsonContent.Create(new { userName = $"admin{index}", password = Password })
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        });

        var statuses = await Task.WhenAll(requests);
        Assert.Single(statuses, status => status == HttpStatusCode.Created);
        Assert.Equal(7, statuses.Count(status => status == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task LoginAttemptsAreRateLimited()
    {
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, "login", "admin", Password, token)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, "login", "admin", Password, token)).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanSignInAfterServerRestart()
    {
        using (var client = factory.CreateClient())
        {
            Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, "initialize", "admin", Password)).StatusCode);
        }
        factory.Dispose();

        using var restartedFactory = CreateFactory();
        using var restartedClient = restartedFactory.CreateClient();
        Assert.True(await IsInitializedAsync(restartedClient));
        using var login = await PostAsync(restartedClient, "login", "admin", Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Empty(await login.Content.ReadAsStringAsync());
        using var session = await restartedClient.GetAsync("/api/admin/auth/session");
        var body = await session.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string userName, string password, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/auth/{path}")
        {
            Content = JsonContent.Create(new { userName, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", token ?? await GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/admin/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<bool> IsInitializedAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/admin/auth/status");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("isInitialized").GetBoolean();
    }

    public void Dispose()
    {
        factory.Dispose();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "data", "database", "homedocs.db")
        }.ToString());
        SqliteConnection.ClearPool(connection);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
