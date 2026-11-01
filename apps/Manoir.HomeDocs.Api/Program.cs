using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

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
app.UseExceptionHandler();
app.UseStatusCodePages();

var publicRoot = Path.GetFullPath(app.Configuration["Frontend:PublicRoot"] ?? "wwwroot/public", app.Environment.ContentRootPath);
var adminRoot = Path.GetFullPath(app.Configuration["Frontend:AdminRoot"] ?? "wwwroot/admin", app.Environment.ContentRootPath);

if (Directory.Exists(adminRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(adminRoot),
        RequestPath = "/admin"
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
});
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/openapi/{**path}", () => Results.NotFound());
app.MapFallback("/health/{**path}", () => Results.NotFound());
app.MapFallback("/admin/{**path:nonfile}", (HttpContext context) => SendIndex(context, adminRoot));
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
