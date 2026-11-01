using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Logic;

namespace Manoir.HomeDocs.Api.Security;

public static class ContentPageEndpoints
{
    public static void MapContentPageEndpoints(this WebApplication app)
    {
        var pages = app.MapGroup("/api/admin/pages").RequireAuthorization();
        pages.MapGet("/", async (ContentPageLogic logic, CancellationToken cancellationToken) =>
            Results.Ok(await logic.ListAsync(cancellationToken)));
        pages.MapGet("/{id:guid}", async (Guid id, ContentPageLogic logic, CancellationToken cancellationToken) =>
        {
            var page = await logic.FindAsync(id, cancellationToken);
            return page is null ? Results.NotFound() : Results.Ok(page);
        });
        pages.MapPost("/", CreateAsync).AddEndpointFilter<AntiforgeryFilter>();
        pages.MapPut("/{id:guid}", UpdateAsync).AddEndpointFilter<AntiforgeryFilter>();
        pages.MapPost("/{id:guid}/publication", SetPublicationAsync).AddEndpointFilter<AntiforgeryFilter>();
        pages.MapPost("/{id:guid}/duplicate", DuplicateAsync).AddEndpointFilter<AntiforgeryFilter>();
        pages.MapDelete("/{id:guid}", DeleteAsync).AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<IResult> CreateAsync(ContentPageInput input, ContentPageLogic logic, CancellationToken cancellationToken)
    {
        var (result, id) = await logic.CreateAsync(input, cancellationToken);
        return result == ContentPageResult.Success
            ? Results.Created($"/api/admin/pages/{id}", new { id })
            : Error(result);
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateRequest request, ContentPageLogic logic, CancellationToken cancellationToken)
    {
        if (request.Page is null) return Error(ContentPageResult.InvalidInput);
        return Result(await logic.UpdateAsync(id, request.Revision, request.Page, cancellationToken));
    }

    private static async Task<IResult> SetPublicationAsync(Guid id, PublicationRequest request, ContentPageLogic logic, CancellationToken cancellationToken) =>
        request.IsPublished is bool isPublished
            ? Result(await logic.SetPublicationAsync(id, request.Revision, isPublished, cancellationToken))
            : Error(ContentPageResult.InvalidInput);

    private static async Task<IResult> DuplicateAsync(Guid id, DuplicateRequest request, ContentPageLogic logic, CancellationToken cancellationToken)
    {
        var (result, newId) = await logic.DuplicateAsync(id, request.Title, request.Slug, cancellationToken);
        return result == ContentPageResult.Success
            ? Results.Created($"/api/admin/pages/{newId}", new { id = newId })
            : Error(result);
    }

    private static async Task<IResult> DeleteAsync(Guid id, long revision, ContentPageLogic logic, CancellationToken cancellationToken) =>
        Result(await logic.DeleteAsync(id, revision, cancellationToken));

    private static IResult Result(ContentPageResult result) =>
        result == ContentPageResult.Success ? Results.NoContent() : Error(result);

    private static IResult Error(ContentPageResult result) => result switch
    {
        ContentPageResult.InvalidInput => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid page metadata or content."),
        ContentPageResult.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Page not found."),
        ContentPageResult.SlugConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The slug is already in use."),
        ContentPageResult.VersionConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The page has changed; reload it before saving."),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };

    public sealed record UpdateRequest(long Revision, ContentPageInput? Page);
    public sealed record PublicationRequest(long Revision, bool? IsPublished);
    public sealed record DuplicateRequest(string? Title, string? Slug);
}
