using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class HostingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"homedocs-tests-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> factory;

    public HostingTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "public", "assets"));
        Directory.CreateDirectory(Path.Combine(root, "public", "api"));
        Directory.CreateDirectory(Path.Combine(root, "public", "admin", "assets"));
        Directory.CreateDirectory(Path.Combine(root, "admin", "assets"));
        File.WriteAllText(Path.Combine(root, "public", "index.html"), "public interface");
        File.WriteAllText(Path.Combine(root, "admin", "index.html"), "admin interface");
        File.WriteAllText(Path.Combine(root, "public", "assets", "public.js"), "public asset");
        File.WriteAllText(Path.Combine(root, "public", "admin", "assets", "public.js"), "wrong interface");
        File.WriteAllText(Path.Combine(root, "public", "api", "unknown"), "not an API response");
        File.WriteAllText(Path.Combine(root, "admin", "assets", "admin.js"), "admin asset");

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["HomeDocs:DataPath"] = Path.Combine(root, "data"),
                    ["Frontend:PublicRoot"] = Path.Combine(root, "public"),
                    ["Frontend:AdminRoot"] = Path.Combine(root, "admin"),
                })));
    }

    [Theory]
    [InlineData("/", "public interface")]
    [InlineData("/some/page", "public interface")]
    [InlineData("/admin/", "admin interface")]
    [InlineData("/admin/some/page", "admin interface")]
    [InlineData("/assets/public.js", "public asset")]
    [InlineData("/admin/assets/admin.js", "admin asset")]
    public async Task ServesCorrectInterface(string path, string expected)
    {
        using var client = factory.CreateClient();
        Assert.Equal(expected, await client.GetStringAsync(path));
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/openapi/unknown")]
    [InlineData("/health/unknown")]
    [InlineData("/admin/assets/missing.js")]
    [InlineData("/admin/assets/public.js")]
    [InlineData("/assets/missing.js")]
    public async Task UnknownApiAndAssetsAreNotSpaFallbacks(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthAndOpenApiAreAvailable()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task AdminRootRedirectsToSlash()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task MissingInterfaceReturnsAnExplicitError()
    {
        File.Delete(Path.Combine(root, "admin", "index.html"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/admin/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreatesPersistentDirectories()
    {
        using var client = factory.CreateClient();
        await client.GetAsync("/health");
        foreach (var directory in new[] { "database", "content", "media", "themes", "css", "backups", "keys" })
        {
            Assert.True(Directory.Exists(Path.Combine(root, "data", directory)));
        }
    }

    [Fact]
    public void DataProtectionKeysSurviveAnotherHost()
    {
        using var firstClient = factory.CreateClient();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        var protectedValue = protector.Protect("local data");
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "data", "keys"), "*.xml"));

        using var secondFactory = factory.WithWebHostBuilder(_ => { });
        using var secondClient = secondFactory.CreateClient();
        var secondProtector = secondFactory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        Assert.Equal("local data", secondProtector.Unprotect(protectedValue));
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
