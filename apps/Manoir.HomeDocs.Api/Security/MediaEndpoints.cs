using Manoir.HomeDocs.Logic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Manoir.HomeDocs.Api.Security;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        var media = app.MapGroup("/api/admin/media").RequireAuthorization();
        media.MapGet("/", async (
            string? q,
            string? folder,
            string? kind,
            MediaLogic logic,
            CancellationToken cancellationToken) =>
            Results.Ok(await logic.ListAsync(q, folder, kind, cancellationToken)));
        media.MapGet("/{id:guid}", async (Guid id, MediaLogic logic, CancellationToken cancellationToken) =>
        {
            var detail = await logic.FindAsync(id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        media.MapPost("/", UploadAsync)
            .AddEndpointFilter<AntiforgeryFilter>()
            .WithMetadata(new RequestSizeLimitAttribute(MediaLogic.MaxBytes + 1024 * 1024));
        media.MapPut("/{id:guid}", UpdateAsync).AddEndpointFilter<AntiforgeryFilter>();
        media.MapDelete("/{id:guid}", DeleteAsync).AddEndpointFilter<AntiforgeryFilter>();

        app.MapGet("/api/public/media/{id:guid}", async (
            Guid id,
            string? context,
            string? profile,
            string? device,
            MediaLogic logic,
            PublicPinAuthorization pinAuthorization,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isAdministrator = httpContext.User.Identity?.IsAuthenticated == true;
            var authorized = context is not null && await pinAuthorization.HasContextGrantAsync(httpContext, context);
            var resolution = await logic.OpenPublicAsync(id, context, profile, device, authorized, isAdministrator, cancellationToken);
            switch (resolution.Result)
            {
                case PublicMediaResult.RequiresPin:
                    httpContext.Response.Headers.CacheControl = "no-store";
                    return Results.Unauthorized();
                case PublicMediaResult.Found:
                    var file = resolution.File!;
                    httpContext.Response.Headers.CacheControl = resolution.IsProtected || isAdministrator
                        ? "no-store"
                        : "private, max-age=300";
                    httpContext.Response.Headers.XContentTypeOptions = "nosniff";
                    httpContext.Response.Headers.ContentDisposition =
                        new ContentDispositionHeaderValue("inline") { FileNameStar = file.Item.Name }.ToString();
                    // The stored content type comes from the file signature, never from the upload.
                    return Results.File(file.Content, file.Item.ContentType);
                default:
                    return Results.NotFound();
            }
        });
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, MediaLogic logic, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType) return Error(MediaOutcome.InvalidInput);
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception error) when (error is InvalidDataException or BadHttpRequestException)
        {
            return Error(MediaOutcome.TooLarge);
        }
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return Error(MediaOutcome.InvalidInput);
        if (file.Length > MediaLogic.MaxBytes) return Error(MediaOutcome.TooLarge);
        byte[] content;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }
        var result = await logic.UploadAsync(
            file.FileName, content, form["title"].ToString().NullIfEmpty(), form["altText"].ToString(),
            form["folder"].ToString(), cancellationToken);
        return result.Outcome == MediaOutcome.Success
            ? Results.Created($"/api/admin/media/{result.Detail!.Media.Id}", result.Detail)
            : Error(result.Outcome);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateRequest request,
        MediaLogic logic,
        CancellationToken cancellationToken)
    {
        var result = await logic.UpdateAsync(id, request.Revision, request.Name, request.Title, request.AltText, request.Folder, cancellationToken);
        return result.Outcome == MediaOutcome.Success ? Results.Ok(result.Detail) : Error(result.Outcome);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        long revision,
        bool? force,
        MediaLogic logic,
        CancellationToken cancellationToken)
    {
        var result = await logic.DeleteAsync(id, revision, force == true, cancellationToken);
        if (result.Outcome == MediaOutcome.InUse)
            return Results.Json(new { title = "The media is used by pages.", usages = result.Usages }, statusCode: StatusCodes.Status409Conflict);
        return result.Outcome == MediaOutcome.Success ? Results.NoContent() : Error(result.Outcome);
    }

    private static IResult Error(MediaOutcome outcome) => outcome switch
    {
        MediaOutcome.InvalidInput => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid media or metadata."),
        MediaOutcome.UnsupportedType => Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Only PNG, JPEG, GIF, WebP images and PDF files are supported."),
        MediaOutcome.TooLarge => Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The file is larger than 20 MB."),
        MediaOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Media not found."),
        MediaOutcome.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The media was changed elsewhere."),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static string? NullIfEmpty(this string value) => value.Length == 0 ? null : value;

    public sealed record UpdateRequest(long Revision, string? Name, string? Title, string? AltText, string? Folder);
}
