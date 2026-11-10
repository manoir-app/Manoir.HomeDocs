using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;

namespace Manoir.HomeDocs.Api.Security;

public static class AppearanceEndpoints
{
    public static void MapAppearanceEndpoints(this WebApplication app)
    {
        var themes = app.MapGroup("/api/admin/themes").RequireAuthorization();
        themes.MapGet("/", async (AppearanceLogic logic, CancellationToken cancellationToken) =>
            Results.Ok(await logic.ListThemesAsync(cancellationToken)));
        themes.MapPost("/", async (ThemeInput input, AppearanceLogic logic, CancellationToken cancellationToken) =>
        {
            var result = await logic.CreateThemeAsync(input, cancellationToken);
            return result == AppearanceResult.Success
                ? Results.Created($"/api/admin/themes/{input.Id!.Trim()}", await logic.FindThemeAsync(input.Id.Trim(), cancellationToken))
                : ToFailure(result);
        }).AddEndpointFilter<AntiforgeryFilter>();
        themes.MapPut("/{id}", async (string id, long revision, ThemeInput input, AppearanceLogic logic, CancellationToken cancellationToken) =>
        {
            var result = await logic.UpdateThemeAsync(id, revision, input, cancellationToken);
            return result == AppearanceResult.Success ? Results.Ok(await logic.FindThemeAsync(id, cancellationToken)) : ToFailure(result);
        }).AddEndpointFilter<AntiforgeryFilter>();
        themes.MapDelete("/{id}", async (string id, long revision, AppearanceLogic logic, CancellationToken cancellationToken) =>
        {
            var result = await logic.DeleteThemeAsync(id, revision, cancellationToken);
            return result == AppearanceResult.Success ? Results.NoContent() : ToFailure(result);
        }).AddEndpointFilter<AntiforgeryFilter>();

        var profiles = app.MapGroup("/api/admin/profiles").RequireAuthorization();
        profiles.MapGet("/", async (AppearanceLogic logic, CancellationToken cancellationToken) =>
            Results.Ok(await logic.ListProfilesAsync(cancellationToken)));
        profiles.MapPut("/{profileId}", async (string profileId, long revision, ProfileSettingsInput input, AppearanceLogic logic, CancellationToken cancellationToken) =>
        {
            var result = await logic.SaveProfileAsync(profileId, revision, input, cancellationToken);
            return result == AppearanceResult.Success ? Results.Ok(await logic.GetProfileAsync(profileId, cancellationToken)) : ToFailure(result);
        }).AddEndpointFilter<AntiforgeryFilter>();
        profiles.MapDelete("/{profileId}", async (string profileId, AppearanceLogic logic, CancellationToken cancellationToken) =>
        {
            var result = await logic.ResetProfileAsync(profileId, cancellationToken);
            return result == AppearanceResult.Success ? Results.Ok(await logic.GetProfileAsync(profileId, cancellationToken)) : ToFailure(result);
        }).AddEndpointFilter<AntiforgeryFilter>();

        app.MapGet("/api/public/appearance", async (
            string? context,
            string? profile,
            string? device,
            AppearanceLogic logic,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var appearance = await logic.ResolveAsync(context, profile, device, cancellationToken);
            return appearance is null ? Results.NotFound() : Results.Ok(appearance);
        });
    }

    private static IResult ToFailure(AppearanceResult result) => result switch
    {
        AppearanceResult.NotFound => Results.NotFound(),
        AppearanceResult.Conflict or AppearanceResult.InUse => Results.Conflict(new { error = result.ToString() }),
        AppearanceResult.BuiltIn => Results.Json(new { error = "BuiltIn" }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(new { error = "InvalidInput" })
    };
}
