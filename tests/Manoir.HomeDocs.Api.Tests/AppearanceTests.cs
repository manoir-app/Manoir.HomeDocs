using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class AppearanceApiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-appearance-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public AppearanceApiTests() =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HomeDocs:DataPath"] = root })));

    private static ThemeInput Contrast(string id, string name = "Contraste") => new(
        id, name, "dark",
        new ThemeColors("#000000", "#111111", "#FFFFFF", "#dddddd", "#ffd400", "#000000", "#444444", "#00ffff"),
        new ThemeStyle("rounded", 4, "none", 32));

    [Fact]
    public async Task ThemesAreValidatedBuiltInsAreImmutableAndUsedThemesCannotBeDeleted()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/themes/")).StatusCode);
        using var admin = await SignedInAdminAsync();

        var list = (await admin.GetFromJsonAsync<Theme[]>("/api/admin/themes/"))!;
        Assert.Equal(["clair", "sombre"], list.Select(theme => theme.Id));
        Assert.All(list, theme => Assert.True(theme.IsBuiltIn));

        using (var builtIn = await SendAsync(admin, HttpMethod.Put, "/api/admin/themes/clair?revision=1", Contrast("clair")))
            Assert.Equal(HttpStatusCode.Forbidden, builtIn.StatusCode);
        using (var builtInDelete = await SendAsync(admin, HttpMethod.Delete, "/api/admin/themes/sombre?revision=1", null))
            Assert.Equal(HttpStatusCode.Forbidden, builtInDelete.StatusCode);
        using (var clash = await SendAsync(admin, HttpMethod.Post, "/api/admin/themes/", Contrast("clair")))
            Assert.Equal(HttpStatusCode.Forbidden, clash.StatusCode);

        foreach (var bad in new[]
        {
            Contrast("Pas Un Slug"),
            Contrast("ok") with { Name = " " },
            Contrast("ok") with { ColorScheme = "sepia" },
            Contrast("ok") with { Colors = Contrast("ok").Colors! with { Accent = "red" } },
            Contrast("ok") with { Colors = Contrast("ok").Colors! with { Text = "#12345" } },
            Contrast("ok") with { Style = new ThemeStyle("comic", 4, "none", 32) },
            Contrast("ok") with { Style = new ThemeStyle("system", 99, "none", 32) },
            Contrast("ok") with { Style = new ThemeStyle("system", 4, "glow", 32) },
            Contrast("ok") with { Style = new ThemeStyle("system", 4, "none", 8) }
        })
        {
            using var response = await SendAsync(admin, HttpMethod.Post, "/api/admin/themes/", bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var created = await SendAsync(admin, HttpMethod.Post, "/api/admin/themes/", Contrast("contraste")))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var theme = (await created.Content.ReadFromJsonAsync<Theme>())!;
            Assert.Equal(1, theme.Revision);
            Assert.False(theme.IsBuiltIn);
            Assert.Equal("#ffffff", theme.Colors.Text);
        }
        using (var duplicate = await SendAsync(admin, HttpMethod.Post, "/api/admin/themes/", Contrast("contraste")))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using (var updated = await SendAsync(admin, HttpMethod.Put, "/api/admin/themes/contraste?revision=1", Contrast("ignore", "Contraste fort")))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var theme = (await updated.Content.ReadFromJsonAsync<Theme>())!;
            Assert.Equal(("contraste", "Contraste fort", 2L), (theme.Id, theme.Name, theme.Revision));
        }
        using (var stale = await SendAsync(admin, HttpMethod.Put, "/api/admin/themes/contraste?revision=1", Contrast("contraste")))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = (await admin.GetFromJsonAsync<ProfileSettings[]>("/api/admin/profiles/"))!.Single(item => item.ProfileId == "tv");
        using (var use = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/tv?revision=0",
                   new ProfileSettingsInput(new ThemeSelection("dark", "clair", "contraste"), current.Layout)))
            Assert.Equal(HttpStatusCode.OK, use.StatusCode);
        using (var inUse = await SendAsync(admin, HttpMethod.Delete, "/api/admin/themes/contraste?revision=2", null))
            Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);

        using (var reset = await SendAsync(admin, HttpMethod.Delete, "/api/admin/profiles/tv", null))
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using (var deleted = await SendAsync(admin, HttpMethod.Delete, "/api/admin/themes/contraste?revision=2", null))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<Theme[]>("/api/admin/themes/"))!.Length);
    }

    [Fact]
    public async Task EachProfileHasDefaultsAndOverridesAreValidatedAndVersioned()
    {
        using var admin = await SignedInAdminAsync();
        var profiles = (await admin.GetFromJsonAsync<ProfileSettings[]>("/api/admin/profiles/"))!;
        Assert.Equal(["desktop", "mobile", "tablet-wall", "tablet-dock", "tv"], profiles.Select(item => item.ProfileId));
        Assert.All(profiles, item => Assert.False(item.IsCustomized));
        var tv = profiles.Single(item => item.ProfileId == "tv");
        var mobile = profiles.Single(item => item.ProfileId == "mobile");
        Assert.True(tv.Layout.FontSizePx > mobile.Layout.FontSizePx);
        Assert.True(tv.Layout.MinTargetPx >= mobile.Layout.MinTargetPx);
        Assert.Equal("remote", tv.Layout.InteractionMode);
        Assert.Equal("touch", mobile.Layout.InteractionMode);

        var layout = mobile.Layout with { FontSizePx = 20, MaxColumns = 2 };
        foreach (var bad in new[]
        {
            new ProfileSettingsInput(mobile.Theme, layout with { FontSizePx = 4 }),
            new ProfileSettingsInput(mobile.Theme, layout with { MaxColumns = 12 }),
            new ProfileSettingsInput(mobile.Theme, layout with { CardSize = "gigantic" }),
            new ProfileSettingsInput(mobile.Theme, layout with { InteractionMode = "voice" }),
            new ProfileSettingsInput(mobile.Theme with { Mode = "auto" }, layout),
            new ProfileSettingsInput(mobile.Theme with { LightThemeId = "inconnu" }, layout)
        })
        {
            using var response = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/mobile?revision=0", bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var unknown = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/frigo?revision=0", new ProfileSettingsInput(mobile.Theme, layout)))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using (var saved = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/mobile?revision=0", new ProfileSettingsInput(mobile.Theme, layout)))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var result = (await saved.Content.ReadFromJsonAsync<ProfileSettings>())!;
            Assert.True(result.IsCustomized);
            Assert.Equal(1, result.Revision);
            Assert.Equal(20, result.Layout.FontSizePx);
        }
        using (var stale = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/mobile?revision=0", new ProfileSettingsInput(mobile.Theme, layout)))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var second = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/mobile?revision=1", new ProfileSettingsInput(mobile.Theme, layout with { FontSizePx = 22 })))
            Assert.Equal(2, (await second.Content.ReadFromJsonAsync<ProfileSettings>())!.Revision);

        // Other profiles are untouched.
        Assert.False((await admin.GetFromJsonAsync<ProfileSettings[]>("/api/admin/profiles/"))!.Single(item => item.ProfileId == "desktop").IsCustomized);

        using var reset = await SendAsync(admin, HttpMethod.Delete, "/api/admin/profiles/mobile", null);
        var back = (await reset.Content.ReadFromJsonAsync<ProfileSettings>())!;
        Assert.False(back.IsCustomized);
        Assert.Equal(mobile.Layout, back.Layout);
    }

    [Fact]
    public async Task PublicAppearanceResolvesPerProfileAndLeavesContextsUntouched()
    {
        using var admin = await SignedInAdminAsync();
        using var anonymous = factory.CreateClient();
        using (var context = await SendAsync(admin, HttpMethod.Post, "/api/admin/contexts/", new { id = "maison", name = "Maison", rootNodeId = (Guid?)null }))
            Assert.Equal(HttpStatusCode.Created, context.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/public/appearance?context=inconnu&profile=tv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/public/appearance?context=maison&profile=frigo")).StatusCode);

        var tvResponse = await anonymous.GetAsync("/api/public/appearance?context=maison&profile=tv");
        Assert.Equal(HttpStatusCode.OK, tvResponse.StatusCode);
        Assert.Contains("no-store", tvResponse.Headers.CacheControl!.ToString());
        var tv = (await tvResponse.Content.ReadFromJsonAsync<PublicAppearance>())!;
        Assert.Equal("dark", tv.ThemeMode);
        Assert.Equal("dark", tv.Light.ColorScheme);
        Assert.Equal("dark", tv.Dark.ColorScheme);

        var desktop = (await anonymous.GetFromJsonAsync<PublicAppearance>("/api/public/appearance?context=maison&profile=desktop"))!;
        Assert.Equal("system", desktop.ThemeMode);
        Assert.Equal("light", desktop.Light.ColorScheme);
        Assert.Equal("dark", desktop.Dark.ColorScheme);

        using (var custom = await SendAsync(admin, HttpMethod.Post, "/api/admin/themes/", Contrast("contraste")))
            Assert.Equal(HttpStatusCode.Created, custom.StatusCode);
        var layout = (await admin.GetFromJsonAsync<ProfileSettings[]>("/api/admin/profiles/"))!.Single(item => item.ProfileId == "tablet-wall").Layout;
        using (var save = await SendAsync(admin, HttpMethod.Put, "/api/admin/profiles/tablet-wall?revision=0",
                   new ProfileSettingsInput(new ThemeSelection("light", "contraste", "sombre"), layout)))
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var wall = (await anonymous.GetFromJsonAsync<PublicAppearance>("/api/public/appearance?context=maison&profile=tablet-wall"))!;
        Assert.Equal("light", wall.ThemeMode);
        Assert.Equal("#ffd400", wall.Light.Colors.Accent);
        Assert.Equal(wall.Light, wall.Dark); // a forced mode shows the same theme in both slots

        // The usage contexts and their navigation are not affected by the appearance.
        Assert.Equal("Maison", (await admin.GetFromJsonAsync<UsageContext[]>("/api/admin/contexts/"))!.Single().Name);
    }

    [Fact]
    public async Task ADeviceOverridesItsOwnDesignOnlyAndNeverTheNavigation()
    {
        using var admin = await SignedInAdminAsync();
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/devices/")).StatusCode);

        // A published page as the root of the context, to compare navigations.
        using (var created = await SendAsync(admin, HttpMethod.Post, "/api/admin/pages/", new
        {
            page = new ContentPageInput("Accueil", "accueil", "page", [], false, "# Accueil"),
            placement = new { parentNodeId = (Guid?)null }
        }))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var pageId = (await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/tree/"))![0].GetProperty("pageId").GetGuid();
        var saved = (await admin.GetFromJsonAsync<ContentPage>($"/api/admin/pages/{pageId}"))!;
        using (var publish = await SendAsync(admin, HttpMethod.Post, $"/api/admin/pages/{pageId}/publication", new { revision = saved.Revision, isPublished = true }))
            Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        var rootNode = (await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/tree/"))![0].GetProperty("id").GetGuid();
        using (var context = await SendAsync(admin, HttpMethod.Post, "/api/admin/contexts/", new { id = "maison", name = "Maison", rootNodeId = rootNode }))
            Assert.Equal(HttpStatusCode.Created, context.StatusCode);

        async Task<string> NavigationAsync(string device) =>
            await anonymous.GetStringAsync($"/api/public/navigation?context=maison&profile=tv&device={device}");
        var navigationBefore = await NavigationAsync("cuisine-tv");
        var pageBefore = await anonymous.GetStringAsync("/api/public/pages/accueil?context=maison&profile=tv&device=cuisine-tv");

        foreach (var bad in new[]
        {
            new DeviceInput("Pas valide", "x", true, null, null),
            new DeviceInput("ok", " ", true, null, null),
            new DeviceInput("ok", "x", true, new DeviceOverride(FontSizePx: 99), null),
            new DeviceInput("ok", "x", true, new DeviceOverride(LightThemeId: "inconnu"), null),
            new DeviceInput("ok", "x", true, null, "@import url(https://exemple.test/x.css);"),
            new DeviceInput("ok", "x", true, null, "</style><script>alert(1)</script>"),
            new DeviceInput("ok", "x", true, null, new string('a', 20_001))
        })
        {
            using var response = await SendAsync(admin, HttpMethod.Post, "/api/admin/devices/", bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var created = await SendAsync(admin, HttpMethod.Post, "/api/admin/devices/", new DeviceInput(
                   "cuisine-tv", "Fire TV cuisine", true, new DeviceOverride(FontSizePx: 36, Mode: "light", MaxColumns: 2), ".public-page { letter-spacing: 0.02em; }")))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var device = (await created.Content.ReadFromJsonAsync<Device>())!;
            Assert.Equal((1L, null), (device.Revision, device.LastSeenAt));
        }
        using (var duplicate = await SendAsync(admin, HttpMethod.Post, "/api/admin/devices/", new DeviceInput("cuisine-tv", "Autre", true, null, null)))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // The invoked device gets its overrides on top of the profile...
        var tv = (await anonymous.GetFromJsonAsync<PublicAppearance>("/api/public/appearance?context=maison&profile=tv&device=cuisine-tv"))!;
        Assert.Equal(36, tv.Layout.FontSizePx);
        Assert.Equal(2, tv.Layout.MaxColumns);
        Assert.Equal(72, tv.Layout.MinTargetPx); // inherited from the profile
        Assert.Equal("light", tv.ThemeMode);
        Assert.Contains("letter-spacing", tv.CustomCss);

        // ...while the reference profile, another device and an unregistered one keep the profile design.
        foreach (var other in new[] { "", "&device=salon-tv", "&device=cuisine-tv-2" })
        {
            var appearance = (await anonymous.GetFromJsonAsync<PublicAppearance>($"/api/public/appearance?context=maison&profile=tv{other}"))!;
            Assert.Equal(28, appearance.Layout.FontSizePx);
            Assert.Equal("dark", appearance.ThemeMode);
            Assert.Null(appearance.CustomCss);
        }
        Assert.Equal(28, (await admin.GetFromJsonAsync<ProfileSettings[]>("/api/admin/profiles/"))!.Single(item => item.ProfileId == "tv").Layout.FontSizePx);

        // Navigation and page access are exactly the same with or without the override.
        Assert.Equal(navigationBefore, await NavigationAsync("cuisine-tv"));
        Assert.Equal(pageBefore, await anonymous.GetStringAsync("/api/public/pages/accueil?context=maison&profile=tv&device=cuisine-tv"));

        // The connection is recorded.
        var listed = (await admin.GetFromJsonAsync<Device[]>("/api/admin/devices/"))!.Single();
        Assert.NotNull(listed.LastSeenAt);
        Assert.Equal(1, listed.Revision);

        // An inactive device is served the profile design.
        using (var disable = await SendAsync(admin, HttpMethod.Put, "/api/admin/devices/cuisine-tv?revision=1",
                   new DeviceInput("ignore", "Fire TV cuisine", false, listed.Override, listed.CustomCss)))
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        var inactive = (await anonymous.GetFromJsonAsync<PublicAppearance>("/api/public/appearance?context=maison&profile=tv&device=cuisine-tv"))!;
        Assert.Equal(28, inactive.Layout.FontSizePx);
        Assert.Null(inactive.CustomCss);
        using (var stale = await SendAsync(admin, HttpMethod.Put, "/api/admin/devices/cuisine-tv?revision=1", new DeviceInput("x", "Autre nom", true, null, null)))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // A theme used by a device override cannot be deleted.
        using (var theme = await SendAsync(admin, HttpMethod.Post, "/api/admin/themes/", Contrast("contraste")))
            Assert.Equal(HttpStatusCode.Created, theme.StatusCode);
        using (var use = await SendAsync(admin, HttpMethod.Put, "/api/admin/devices/cuisine-tv?revision=2",
                   new DeviceInput("x", "Fire TV cuisine", true, new DeviceOverride(DarkThemeId: "contraste"), "")))
            Assert.Equal(HttpStatusCode.OK, use.StatusCode);
        using (var inUse = await SendAsync(admin, HttpMethod.Delete, "/api/admin/themes/contraste?revision=1", null))
            Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);

        using (var deleted = await SendAsync(admin, HttpMethod.Delete, "/api/admin/devices/cuisine-tv?revision=3", null))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<Device[]>("/api/admin/devices/"))!);
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
