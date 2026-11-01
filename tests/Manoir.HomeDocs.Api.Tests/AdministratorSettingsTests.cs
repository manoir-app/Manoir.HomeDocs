using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class AdministratorSettingsTests : IDisposable
{
    private const string InitialPassword = "initial-password-long";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-settings-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public AdministratorSettingsTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));
    }

    [Fact]
    public async Task SettingsRequireAuthenticationAndAntiforgeryAndPersistLanguage()
    {
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.GetAsync("/api/admin/settings/");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var admin = factory.CreateClient();
        await InitializeAndSignInAsync(admin);
        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/admin/settings/");
        Assert.Equal("admin", settings.GetProperty("userName").GetString());
        Assert.Equal("fr", settings.GetProperty("language").GetString());
        Assert.False(settings.GetProperty("pinConfigured").GetBoolean());
        Assert.DoesNotContain("Hash", settings.ToString(), StringComparison.OrdinalIgnoreCase);

        using var noCsrf = await admin.PutAsJsonAsync("/api/admin/settings/language", new { language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var unsupported = await SendAsync(admin, "/api/admin/settings/language", new { language = "de" });
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        using var update = await SendAsync(admin, "/api/admin/settings/language", new { language = "en" });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        settings = await admin.GetFromJsonAsync<JsonElement>("/api/admin/settings/");
        Assert.Equal("en", settings.GetProperty("language").GetString());
    }

    [Fact]
    public async Task AccountAndPinChangesRequireReauthenticationAndNeverReturnSecrets()
    {
        using var admin = factory.CreateClient();
        await InitializeAndSignInAsync(admin);
        const string newPassword = "replacement-password-long";

        using var wrongPassword = await SendAsync(admin, "/api/admin/settings/account",
            new { currentPassword = "wrong-current-password", userName = "new-admin", newPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        using var invalidNewPassword = await SendAsync(admin, "/api/admin/settings/account",
            new { currentPassword = InitialPassword, userName = "new-admin", newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidNewPassword.StatusCode);
        using var rename = await SendAsync(admin, "/api/admin/settings/account",
            new { currentPassword = InitialPassword, userName = "new-admin", newPassword = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        var profile = await admin.GetFromJsonAsync<JsonElement>("/api/admin/settings/");
        Assert.Equal("new-admin", profile.GetProperty("userName").GetString());
        Assert.DoesNotContain(InitialPassword, profile.ToString());
        using var oldLogin = factory.CreateClient();
        using (var oldToken = await TokenAsync(oldLogin))
        using (var oldResponse = await PostLoginAsync(oldLogin, "admin", InitialPassword, oldToken))
            Assert.Equal(HttpStatusCode.Unauthorized, oldResponse.StatusCode);
        using var newLogin = factory.CreateClient();
        using (var newToken = await TokenAsync(newLogin))
        using (var newResponse = await PostLoginAsync(newLogin, "new-admin", InitialPassword, newToken))
            Assert.Equal(HttpStatusCode.NoContent, newResponse.StatusCode);

        using var invalidPin = await SendAsync(admin, "/api/admin/settings/pin",
            new { currentPassword = InitialPassword, pin = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPin.StatusCode);
        using var wrongReauth = await SendAsync(admin, "/api/admin/settings/pin",
            new { currentPassword = "wrong-current-password", pin = "012345" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongReauth.StatusCode);
        using var setPin = await SendAsync(admin, "/api/admin/settings/pin",
            new { currentPassword = InitialPassword, pin = "012345" });
        Assert.Equal(HttpStatusCode.NoContent, setPin.StatusCode);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "database", "homedocs.db")
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT PinHash FROM Administrators WHERE Id = 1";
            var storedHash = (string?)await command.ExecuteScalarAsync();
            Assert.NotNull(storedHash);
            Assert.NotEqual("012345", storedHash);
        }
        profile = await admin.GetFromJsonAsync<JsonElement>("/api/admin/settings/");
        Assert.True(profile.GetProperty("pinConfigured").GetBoolean());
        Assert.DoesNotContain("012345", profile.ToString());

        using var removePin = await SendAsync(admin, "/api/admin/settings/pin",
            new { currentPassword = InitialPassword, pin = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, removePin.StatusCode);
        profile = await admin.GetFromJsonAsync<JsonElement>("/api/admin/settings/");
        Assert.False(profile.GetProperty("pinConfigured").GetBoolean());
    }

    [Fact]
    public async Task PasswordCanBeChangedAndCurrentSessionIsRenewed()
    {
        using var admin = factory.CreateClient();
        await InitializeAndSignInAsync(admin);
        const string newPassword = "updated-password-long";
        using var change = await SendAsync(admin, "/api/admin/settings/account",
            new { currentPassword = InitialPassword, userName = "admin", newPassword });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        using var session = await admin.GetAsync("/api/admin/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        using var oldLogin = factory.CreateClient();
        using (var token = await TokenAsync(oldLogin))
        using (var response = await PostLoginAsync(oldLogin, "admin", InitialPassword, token))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var newLogin = factory.CreateClient();
        using (var token = await TokenAsync(newLogin))
        using (var response = await PostLoginAsync(newLogin, "admin", newPassword, token))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task InitializeAndSignInAsync(HttpClient client)
    {
        using var setupToken = await TokenAsync(client);
        using var initialize = await PostAsync(client, "/api/admin/auth/initialize",
            new { userName = "admin", password = InitialPassword }, setupToken);
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        using var loginToken = await TokenAsync(client);
        using var login = await PostLoginAsync(client, "admin", InitialPassword, loginToken);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object payload)
    {
        using var token = await TokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", token.Value);
        return await client.SendAsync(request);
    }

    private static async Task<CsrfToken> TokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/admin/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new CsrfToken(json.RootElement.GetProperty("token").GetString()!);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object payload, CsrfToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", token.Value);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string userName, string password, CsrfToken token) =>
        PostAsync(client, "/api/admin/auth/login", new { userName, password }, token);

    private sealed record CsrfToken(string Value) : IDisposable
    {
        public void Dispose() { }
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
