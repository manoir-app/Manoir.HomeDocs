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
            .Select(administrator => new AdministratorCredentials(
                administrator.UserName, administrator.NormalizedUserName, administrator.PasswordHash,
                administrator.PinHash, administrator.Language))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> UpdateCredentialsAsync(
        string currentNormalizedUserName,
        string userName,
        string normalizedUserName,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var administrator = await context.Administrators.SingleOrDefaultAsync(
            item => item.Id == 1 && item.NormalizedUserName == currentNormalizedUserName, cancellationToken);
        if (administrator is null) return false;
        administrator.UserName = userName;
        administrator.NormalizedUserName = normalizedUserName;
        administrator.PasswordHash = passwordHash;
        administrator.Revision++;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> UpdatePinHashAsync(string normalizedUserName, string? pinHash, CancellationToken cancellationToken = default)
    {
        var administrator = await context.Administrators.SingleOrDefaultAsync(
            item => item.Id == 1 && item.NormalizedUserName == normalizedUserName, cancellationToken);
        if (administrator is null) return false;
        administrator.PinHash = pinHash;
        administrator.Revision++;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> UpdateLanguageAsync(string normalizedUserName, string language, CancellationToken cancellationToken = default)
    {
        var administrator = await context.Administrators.SingleOrDefaultAsync(
            item => item.Id == 1 && item.NormalizedUserName == normalizedUserName, cancellationToken);
        if (administrator is null) return false;
        administrator.Language = language;
        administrator.Revision++;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }
}
