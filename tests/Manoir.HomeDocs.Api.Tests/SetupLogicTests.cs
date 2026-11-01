using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class SetupLogicTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReportsStatusFromOperations(bool initialized)
    {
        var operations = new StubAdministratorOperations { Initialized = initialized };
        var logic = new SetupLogic(operations);

        var status = await logic.GetStatusAsync();

        Assert.Equal(initialized, status.IsInitialized);
        Assert.True(operations.WasCalled);
    }

    private sealed class StubAdministratorOperations : IAdministratorOperations
    {
        public bool Initialized { get; init; }
        public bool WasCalled { get; private set; }

        public Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Initialized);
        }

        public Task<bool> TryCreateInitialAsync(AdministratorCredentials administrator, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministratorCredentials?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetPinHashAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateCredentialsAsync(string currentNormalizedUserName, string userName, string normalizedUserName, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdatePinHashAsync(string normalizedUserName, string? pinHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateLanguageAsync(string normalizedUserName, string language, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
