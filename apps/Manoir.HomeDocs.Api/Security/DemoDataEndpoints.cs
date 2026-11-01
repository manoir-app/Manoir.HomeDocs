using Manoir.HomeDocs.Logic;

namespace Manoir.HomeDocs.Api.Security;

public static class DemoDataEndpoints
{
    public static void MapDemoDataEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/demo/install", async (
            DemoDataLogic logic,
            CancellationToken cancellationToken) =>
        {
            var result = await logic.InstallAsync(cancellationToken);
            return result switch
            {
                DemoDataInstallResult.Installed => Results.Ok(new { status = "installed" }),
                DemoDataInstallResult.AlreadyInstalled => Results.Ok(new { status = "alreadyInstalled" }),
                DemoDataInstallResult.Conflict => Results.Conflict(new { error = "demoConflict" }),
                _ => throw new ArgumentOutOfRangeException(nameof(result))
            };
        })
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryFilter>()
            .RequireRateLimiting("admin-sensitive");
    }
}
