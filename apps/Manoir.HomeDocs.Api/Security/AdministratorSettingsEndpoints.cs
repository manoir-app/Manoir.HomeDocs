using System.Security.Claims;
using Manoir.HomeDocs.Logic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Manoir.HomeDocs.Api.Security;

public static class AdministratorSettingsEndpoints
{
    public static void MapAdministratorSettingsEndpoints(this WebApplication app)
    {
        var settings = app.MapGroup("/api/admin/settings").RequireAuthorization();
        settings.MapGet("/", async (ClaimsPrincipal user, AdministratorLogic logic, CancellationToken cancellationToken) =>
        {
            var result = await logic.GetSettingsAsync(user.Identity?.Name, cancellationToken);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
        settings.MapPut("/language", async (LanguageRequest request, ClaimsPrincipal user, AdministratorLogic logic, CancellationToken cancellationToken) =>
            Result(await logic.UpdateLanguageAsync(user.Identity?.Name, request.Language, cancellationToken)))
            .AddEndpointFilter<AntiforgeryFilter>();
        settings.MapPut("/account", UpdateAccountAsync)
            .AddEndpointFilter<AntiforgeryFilter>()
            .RequireRateLimiting("admin-sensitive");
        settings.MapPut("/pin", async (PinRequest request, ClaimsPrincipal user, AdministratorLogic logic, CancellationToken cancellationToken) =>
            Result(await logic.UpdatePinAsync(user.Identity?.Name, request.CurrentPassword, request.Pin, cancellationToken)))
            .AddEndpointFilter<AntiforgeryFilter>()
            .RequireRateLimiting("admin-sensitive");
    }

    private static async Task<IResult> UpdateAccountAsync(
        AccountRequest request,
        ClaimsPrincipal user,
        AdministratorLogic logic,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await logic.UpdateCredentialsAsync(
            user.Identity?.Name, request.CurrentPassword, request.UserName, request.NewPassword, cancellationToken);
        if (result != AdministratorChangeResult.Updated) return Result(result);

        var userName = request.UserName!.Trim();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userName), new Claim(ClaimTypes.Name, userName)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.NoContent();
    }

    private static IResult Result(AdministratorChangeResult result) => result switch
    {
        AdministratorChangeResult.Updated => Results.NoContent(),
        AdministratorChangeResult.InvalidInput => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["settings"] = ["The supplied settings are invalid."]
        }),
        AdministratorChangeResult.InvalidCurrentPassword => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Current password is incorrect."),
        AdministratorChangeResult.NotFound => Results.Unauthorized(),
        AdministratorChangeResult.Conflict => Results.Conflict(),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };

    public sealed record LanguageRequest(string? Language);
    public sealed record AccountRequest(string? CurrentPassword, string? UserName, string? NewPassword);
    public sealed record PinRequest(string? CurrentPassword, string? Pin);
}
