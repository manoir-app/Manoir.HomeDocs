using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Api.Security;

public static class UsageContextEndpoints
{
    public static void MapUsageContextEndpoints(this WebApplication app)
    {
        var contexts = app.MapGroup("/api/admin/contexts").RequireAuthorization();
        contexts.MapGet("/", async (UsageContextLogic logic, CancellationToken cancellationToken) =>
            Results.Ok(await logic.ListAsync(cancellationToken)));
        contexts.MapGet("/{id}", async (string id, UsageContextLogic logic, CancellationToken cancellationToken) =>
        {
            var context = await logic.FindAsync(id, cancellationToken);
            return context is null ? Results.NotFound() : Results.Ok(context);
        });
        contexts.MapPost("/", CreateAsync).AddEndpointFilter<AntiforgeryFilter>();
        contexts.MapPut("/{currentId}", UpdateAsync).AddEndpointFilter<AntiforgeryFilter>();
        contexts.MapDelete("/{id}", DeleteAsync).AddEndpointFilter<AntiforgeryFilter>();

        var publicApi = app.MapGroup("/api/public");
        publicApi.MapGet("/navigation", async (
            string? context,
            string? profile,
            string? device,
            UsageContextLogic logic,
            PublicPinAuthorization pinAuthorization,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var authorized = await HasGrantForContextAsync(context, pinAuthorization, httpContext);
            var navigation = await logic.GetNavigationAsync(context, profile, device, authorized, cancellationToken);
            return navigation is null ? Results.NotFound() : Results.Ok(navigation);
        });
        publicApi.MapGet("/pages/{slug}", async (
            string slug,
            string? context,
            string? profile,
            string? device,
            UsageContextLogic logic,
            PublicPinAuthorization pinAuthorization,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var authorized = await HasGrantForContextAsync(context, pinAuthorization, httpContext);
            var resolution = await logic.ResolvePageAsync(context, slug, profile, device, authorized, cancellationToken);
            return resolution.Result switch
            {
                PublicPageResult.Found => Results.Ok(resolution.Page),
                PublicPageResult.Redirect when context is not null && resolution.RedirectSlug is not null =>
                    Results.Redirect(BuildPageUrl(resolution.RedirectSlug, context, profile!, device)),
                PublicPageResult.RequiresPin => Results.Unauthorized(),
                _ => Results.NotFound()
            };
        });
        publicApi.MapGet("/search", async (
            string? q,
            string? context,
            string? profile,
            string? device,
            SearchLogic search,
            PublicPinAuthorization pinAuthorization,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var authorized = await HasGrantForContextAsync(context, pinAuthorization, httpContext);
            var response = await search.SearchAsync(context, profile, device, q, authorized, SearchLogic.DefaultLimit, cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        });
        publicApi.MapPost("/access/pin", VerifyPinAsync)
            .AddEndpointFilter<AntiforgeryFilter>()
            .RequireRateLimiting("public-pin");
        publicApi.MapGet("/access/status", async (
            string? context,
            string? profile,
            string? device,
            UsageContextLogic contexts,
            PublicPinAuthorization pinAuthorization,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            if (context is null)
            {
                await pinAuthorization.RevokeAsync(httpContext);
                return Results.NotFound();
            }
            var authorized = await pinAuthorization.HasContextGrantAsync(httpContext, context);
            if (!await contexts.IsKnownInvocationAsync(context, profile, device, cancellationToken))
                return Results.NotFound();
            return authorized ? Results.NoContent() : Results.Unauthorized();
        });
        publicApi.MapPost("/access/logout", (HttpContext context, PublicPinAuthorization pinAuthorization) =>
            pinAuthorization.RevokeAsync(context))
            .AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<IResult> VerifyPinAsync(
        PinRequest request,
        UsageContextLogic contexts,
        Manoir.HomeDocs.Logic.AdministratorLogic administrator,
        PublicPinAuthorization pinAuthorization,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!await contexts.IsKnownInvocationAsync(request.Context, request.Profile, request.Device, cancellationToken))
            return Results.NotFound();
        if (!await administrator.VerifyPinAsync(request.Pin, cancellationToken))
            return Results.Unauthorized();
        await pinAuthorization.GrantAsync(httpContext, request.Context!);
        return Results.NoContent();
    }

    private static string BuildPageUrl(string slug, string context, string profile, string? device)
    {
        var query = $"context={Uri.EscapeDataString(context)}&profile={Uri.EscapeDataString(profile)}";
        if (device is not null) query += $"&device={Uri.EscapeDataString(device)}";
        return $"/api/public/pages/{Uri.EscapeDataString(slug)}?{query}";
    }

    private static async Task<bool> HasGrantForContextAsync(
        string? contextId,
        PublicPinAuthorization pinAuthorization,
        HttpContext httpContext)
    {
        if (contextId is not null)
            return await pinAuthorization.HasContextGrantAsync(httpContext, contextId);
        await pinAuthorization.RevokeAsync(httpContext);
        return false;
    }

    private static async Task<IResult> CreateAsync(ContextRequest request, UsageContextLogic logic, CancellationToken cancellationToken)
    {
        if (request.Id is null || request.Name is null) return Error(ContextWriteResult.InvalidInput);
        var result = await logic.SaveAsync(null, request.Id, request.Name, request.RootNodeId, cancellationToken);
        return result == ContextWriteResult.Success
            ? Results.Created($"/api/admin/contexts/{request.Id}", new { id = request.Id })
            : Error(result);
    }

    private static async Task<IResult> UpdateAsync(
        string currentId,
        ContextRequest request,
        UsageContextLogic logic,
        CancellationToken cancellationToken)
    {
        if (request.Id is null || request.Name is null) return Error(ContextWriteResult.InvalidInput);
        return Result(await logic.SaveAsync(currentId, request.Id, request.Name, request.RootNodeId, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(string id, UsageContextLogic logic, CancellationToken cancellationToken) =>
        Result(await logic.DeleteAsync(id, cancellationToken));

    private static IResult Result(ContextWriteResult result) =>
        result == ContextWriteResult.Success ? Results.NoContent() : Error(result);

    private static IResult Error(ContextWriteResult result) => result switch
    {
        ContextWriteResult.InvalidInput => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["context"] = ["The context identifier or name is invalid."]
        }),
        ContextWriteResult.NotFound => Results.NotFound(),
        ContextWriteResult.Conflict => Results.Conflict(),
        ContextWriteResult.InUse => Results.Conflict(),
        ContextWriteResult.InvalidRoot => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["rootNodeId"] = ["The root must be an existing page available in this context."]
        }, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };

    public sealed record ContextRequest(string? Id, string? Name, Guid? RootNodeId);
    public sealed record PinRequest(string? Context, string? Profile, string? Device, string? Pin);
}
