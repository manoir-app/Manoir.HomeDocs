using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Api.Security;

public static class PageTreeEndpoints
{
    public static void MapPageTreeEndpoints(this WebApplication app)
    {
        var tree = app.MapGroup("/api/admin/tree").RequireAuthorization();
        tree.MapGet("/", async (PageTreeLogic logic, CancellationToken cancellationToken) =>
            Results.Ok(await logic.ListAsync(cancellationToken)));
        tree.MapGet("/nodes/{id:guid}/target", async (Guid id, PageTreeLogic logic, CancellationToken cancellationToken) =>
        {
            var pageId = await logic.ResolveTargetPageAsync(id, cancellationToken);
            return pageId is null ? Results.NotFound() : Results.Ok(new { pageId });
        });
        tree.MapPost("/links", async (
            CreateLinkRequest request, PageTreeLogic logic, CancellationToken cancellationToken) =>
            Result(await logic.CreateLinkAsync(request.PageId, request.ParentNodeId, cancellationToken)))
            .AddEndpointFilter<AntiforgeryFilter>();
        tree.MapPut("/nodes/{id:guid}", async (
            Guid id, MoveNodeRequest request, PageTreeLogic logic, CancellationToken cancellationToken) =>
            request.Position < 0
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["position"] = ["Position must not be negative."] })
                : Result(await logic.MoveAsync(id, request.ParentNodeId, request.Position, cancellationToken)))
            .AddEndpointFilter<AntiforgeryFilter>();
        tree.MapDelete("/nodes/{id:guid}", async (
            Guid id, PageTreeLogic logic, CancellationToken cancellationToken) =>
            Result(await logic.RemoveAsync(id, cancellationToken)))
            .AddEndpointFilter<AntiforgeryFilter>();
    }

    private static IResult Result(PageTreeResult result) => result switch
    {
        PageTreeResult.Success => Results.NoContent(),
        PageTreeResult.PageNotFound => Results.NotFound(),
        PageTreeResult.NodeNotFound => Results.NotFound(),
        PageTreeResult.ParentNotFound => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Parent node not found."),
        PageTreeResult.HasChildren => Results.Conflict(),
        PageTreeResult.CannotRemovePageNode => Results.Conflict(),
        PageTreeResult.RootInUse => Results.Conflict(),
        PageTreeResult.InvalidMove => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["parentNodeId"] = ["This move would create a cycle in the page tree."]
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };

    public sealed record CreateLinkRequest(Guid PageId, Guid? ParentNodeId);
    public sealed record MoveNodeRequest(Guid? ParentNodeId, int Position);
}
