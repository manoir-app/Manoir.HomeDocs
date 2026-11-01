using System.Security.Claims;
using Manoir.HomeDocs.Logic;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Manoir.HomeDocs.Api.Security;

public static class AdministratorEndpoints
{
    public static void MapAdministratorEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/admin/auth");
        auth.MapGet("/status", async (SetupLogic setup, CancellationToken cancellationToken) =>
            Results.Ok(await setup.GetStatusAsync(cancellationToken)));
        auth.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        auth.MapPost("/initialize", InitializeAsync)
            .AddEndpointFilter<AntiforgeryFilter>()
            .RequireRateLimiting("admin-auth");
        auth.MapPost("/login", LoginAsync)
            .AddEndpointFilter<AntiforgeryFilter>()
            .RequireRateLimiting("admin-auth");
        auth.MapGet("/session", (ClaimsPrincipal user) => Results.Ok(new { userName = user.Identity?.Name }))
            .RequireAuthorization();
        auth.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<IResult> InitializeAsync(AdministratorRequest request, AdministratorLogic logic, CancellationToken cancellationToken)
    {
        return await logic.InitializeAsync(request.UserName, request.Password, cancellationToken) switch
        {
            InitializationResult.Created => Results.StatusCode(StatusCodes.Status201Created),
            InitializationResult.AlreadyInitialized => Results.Conflict(),
            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["User name is required and password must contain 12 to 1024 characters."]
            })
        };
    }

    private static async Task<IResult> LoginAsync(AdministratorRequest request, AdministratorLogic logic, HttpContext context, CancellationToken cancellationToken)
    {
        var userName = await logic.AuthenticateAsync(request.UserName, request.Password, cancellationToken);
        if (userName is null)
        {
            return Results.Unauthorized();
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userName), new Claim(ClaimTypes.Name, userName)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    public sealed class AdministratorRequest
    {
        public string? UserName { get; init; }
        public string? Password { get; init; }
    }
}
