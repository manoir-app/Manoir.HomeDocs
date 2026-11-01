using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public static class PersistenceServices
{
    public static IServiceCollection AddHomeDocsSqlite(this IServiceCollection services, Func<IServiceProvider, string> databasePath)
    {
        services.AddLogging();
        services.AddDbContext<HomeDocsDbContext>((provider, options) =>
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath(provider) }.ToString();
            options.UseSqlite(connectionString);
        });
        services.AddScoped<IAdministratorOperations, AdministratorOperations>();
        services.AddScoped(provider => new ContentFileStore(databasePath(provider), provider.GetRequiredService<ILogger<ContentFileStore>>()));
        services.AddScoped<IContentPageOperations, ContentPageOperations>();
        services.AddScoped<IPageTreeOperations, PageTreeOperations>();
        services.AddScoped<PageTreeLogic>();
        services.AddScoped<IUsageContextOperations, UsageContextOperations>();
        services.AddScoped<UsageContextLogic>();
        services.AddScoped<ContentPageLogic>();
        services.AddScoped<SetupLogic>();
        services.AddScoped<AdministratorLogic>();
        services.AddScoped<DemoDataLogic>();
        return services;
    }

    public static async Task MigrateHomeDocsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HomeDocsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
