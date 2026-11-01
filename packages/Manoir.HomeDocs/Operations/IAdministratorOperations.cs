namespace Manoir.HomeDocs.Operations;

public interface IAdministratorOperations
{
    Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default);

    Task<bool> TryCreateInitialAsync(AdministratorCredentials administrator, CancellationToken cancellationToken = default);

    Task<AdministratorCredentials?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default);
}
