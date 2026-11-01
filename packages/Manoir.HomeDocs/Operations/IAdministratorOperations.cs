namespace Manoir.HomeDocs.Operations;

public interface IAdministratorOperations
{
    Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default);

    Task<bool> TryCreateInitialAsync(AdministratorCredentials administrator, CancellationToken cancellationToken = default);

    Task<AdministratorCredentials?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default);

    Task<bool> UpdateCredentialsAsync(
        string currentNormalizedUserName,
        string userName,
        string normalizedUserName,
        string passwordHash,
        CancellationToken cancellationToken = default);

    Task<bool> UpdatePinHashAsync(string normalizedUserName, string? pinHash, CancellationToken cancellationToken = default);

    Task<bool> UpdateLanguageAsync(string normalizedUserName, string language, CancellationToken cancellationToken = default);
}
