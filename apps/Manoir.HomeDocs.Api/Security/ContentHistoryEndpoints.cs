using Manoir.HomeDocs.Logic;

namespace Manoir.HomeDocs.Api.Security;

public static class ContentHistoryEndpoints
{
    public static void MapContentHistoryEndpoints(this WebApplication app)
    {
        var pages = app.MapGroup("/api/admin/pages").RequireAuthorization();
        pages.MapGet("/{id:guid}/versions", async (Guid id, ContentHistoryLogic logic, CancellationToken cancellationToken) =>
        {
            var versions = await logic.ListAsync(id, cancellationToken);
            return versions is null ? Results.NotFound() : Results.Ok(versions);
        });
        pages.MapGet("/{id:guid}/versions/{revision:long}", async (
            Guid id,
            long revision,
            ContentHistoryLogic logic,
            CancellationToken cancellationToken) =>
        {
            var version = await logic.FindAsync(id, revision, cancellationToken);
            return version is null ? Results.NotFound() : Results.Ok(version);
        });
        pages.MapPost("/{id:guid}/versions/{revision:long}/restore", async (
            Guid id,
            long revision,
            RestoreRequest request,
            ContentHistoryLogic logic,
            CancellationToken cancellationToken) =>
            await logic.RestoreAsync(id, request.Revision, revision, cancellationToken) switch
            {
                HistoryResult.Success => Results.NoContent(),
                HistoryResult.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Page or version not found."),
                HistoryResult.VersionConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The page changed after you opened it."),
                HistoryResult.SlugConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The slug is already used."),
                HistoryResult.RootInUse => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The page is the navigation root of a context that the version excludes."),
                _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The version cannot be restored.")
            }).AddEndpointFilter<AntiforgeryFilter>();

        pages.MapGet("/{id:guid}/links", async (Guid id, PageLinkLogic logic, CancellationToken cancellationToken) =>
        {
            var report = await logic.ReportAsync(id, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });
        app.MapGet("/api/admin/links/broken", async (PageLinkLogic logic, CancellationToken cancellationToken) =>
            Results.Ok(await logic.BrokenLinksAsync(cancellationToken))).RequireAuthorization();
    }

    public sealed record RestoreRequest(long Revision);
}
