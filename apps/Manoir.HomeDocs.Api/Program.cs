using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.FileProviders;
using Manoir.HomeDocs.Persistence.Sqlite;
using Manoir.HomeDocs.Api.Security;
using Manoir.HomeDocs.Operations;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
builder.Services.AddSingleton<IPasswordOperations, PasswordOperations>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "HomeDocs.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/admin/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("admin-auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("admin-sensitive", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHomeDocsSqlite(provider =>
    Path.Combine(GetDataPath(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IWebHostEnvironment>()), "database", "homedocs.db"));

builder.Services.AddDataProtection();
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IConfiguration, IWebHostEnvironment, ILoggerFactory>((options, configuration, environment, loggerFactory) =>
    {
        var keysPath = Path.Combine(GetDataPath(configuration, environment), "keys");
        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keysPath), loggerFactory);
    });

var app = builder.Build();
var dataPath = GetDataPath(app.Configuration, app.Environment);
foreach (var directory in new[] { "database", "content", "media", "themes", "css", "backups", "keys" })
{
    Directory.CreateDirectory(Path.Combine(dataPath, directory));
}
await app.Services.MigrateHomeDocsAsync();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var publicRoot = Path.GetFullPath(app.Configuration["Frontend:PublicRoot"] ?? "wwwroot/public", app.Environment.ContentRootPath);
var adminRoot = Path.GetFullPath(app.Configuration["Frontend:AdminRoot"] ?? "wwwroot/admin", app.Environment.ContentRootPath);

var adminAssets = Path.Combine(adminRoot, "assets");
if (Directory.Exists(adminAssets))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(adminAssets),
        RequestPath = "/admin/assets"
    });
}
if (Directory.Exists(publicRoot))
{
    app.UseWhen(context =>
        !context.Request.Path.StartsWithSegments("/admin") &&
        !context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/health") &&
        !context.Request.Path.StartsWithSegments("/openapi"),
        branch => branch.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(publicRoot) }));
}

app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapAdministratorEndpoints();
app.MapAdministratorSettingsEndpoints();
app.MapContentPageEndpoints();
app.MapGet("/admin/login", (HttpContext context) => SendIndex(context, adminRoot));
app.MapGet("/admin/setup", (HttpContext context) => SendIndex(context, adminRoot));
app.MapGet("/admin", async (HttpContext context) =>
{
    if (context.Request.Path.Value?.EndsWith('/') == true)
    {
        await SendIndex(context, adminRoot);
    }
    else
    {
        await Results.Redirect("/admin/").ExecuteAsync(context);
    }
}).RequireAuthorization();
app.MapFallback("/api/admin/{**path}", () => Results.NotFound()).RequireAuthorization();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/openapi/{**path}", () => Results.NotFound());
app.MapFallback("/health/{**path}", () => Results.NotFound());
app.MapFallback("/admin/{**path:nonfile}", (HttpContext context) => SendIndex(context, adminRoot)).RequireAuthorization();
app.MapFallback("/{**path:nonfile}", (HttpContext context) => SendIndex(context, publicRoot));

app.Run();

static string GetDataPath(IConfiguration configuration, IWebHostEnvironment environment) =>
    Path.GetFullPath(configuration["HomeDocs:DataPath"] ?? "data", environment.ContentRootPath);

static async Task SendIndex(HttpContext context, string root)
{
    var index = Path.Combine(root, "index.html");
    if (!File.Exists(index))
    {
        await Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "L'interface n'est pas construite.").ExecuteAsync(context);
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(index);
}

public partial class Program;
