using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public sealed class SetupLogic(IAdministratorOperations administrators)
{
    public async Task<SetupStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        new(await administrators.IsInitializedAsync(cancellationToken));
}
