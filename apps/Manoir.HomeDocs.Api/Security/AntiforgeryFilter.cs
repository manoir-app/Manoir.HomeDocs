using Microsoft.AspNetCore.Antiforgery;

namespace Manoir.HomeDocs.Api.Security;

public sealed class AntiforgeryFilter(IAntiforgery antiforgery, ILogger<AntiforgeryFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning("A request was rejected because antiforgery validation failed.");
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid antiforgery token.");
        }

        return await next(context);
    }
}
