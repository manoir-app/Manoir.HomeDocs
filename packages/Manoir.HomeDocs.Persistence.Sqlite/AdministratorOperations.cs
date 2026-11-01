using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public sealed class AdministratorOperations(HomeDocsDbContext context) : IAdministratorOperations
{
    public Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default) =>
        context.Administrators.AnyAsync(cancellationToken);

    public async Task<bool> TryCreateInitialAsync(AdministratorCredentials administrator, CancellationToken cancellationToken = default)
    {
        var rows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Administrators (Id, UserName, NormalizedUserName, PasswordHash) VALUES (1, {administrator.UserName}, {administrator.NormalizedUserName}, {administrator.PasswordHash}) ON CONFLICT(Id) DO NOTHING",
            cancellationToken);
        return rows == 1;
    }

    public async Task<AdministratorCredentials?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default) =>
        await context.Administrators.AsNoTracking()
            .Where(administrator => administrator.NormalizedUserName == normalizedUserName)
            .Select(administrator => new AdministratorCredentials(administrator.UserName, administrator.NormalizedUserName, administrator.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken);
}
