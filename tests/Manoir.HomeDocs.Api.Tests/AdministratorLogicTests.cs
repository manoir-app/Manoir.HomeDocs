using Manoir.HomeDocs.Logic;
using Manoir.HomeDocs.Operations;
using Xunit;

namespace Manoir.HomeDocs.Api.Tests;

public sealed class AdministratorLogicTests
{
    [Fact]
    public async Task InitializationNormalizesNameAndStoresOnlyHash()
    {
        var operations = new StubAdministrators();
        var logic = new AdministratorLogic(operations, new StubPasswords());

        Assert.Equal(InitializationResult.Created, await logic.InitializeAsync(" admin ", "long password"));
        Assert.Equal(new AdministratorCredentials("admin", "ADMIN", "hashed"), operations.Created);
    }

    [Theory]
    [InlineData("", "long password")]
    [InlineData("user\nname", "long password")]
    [InlineData("admin", "short")]
    public async Task InvalidInputDoesNotAccessStorage(string userName, string password)
    {
        var operations = new StubAdministrators();
        var logic = new AdministratorLogic(operations, new StubPasswords());

        Assert.Equal(InitializationResult.InvalidInput, await logic.InitializeAsync(userName, password));
        Assert.Equal(0, operations.Calls);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ExistingOrConcurrentAdministratorCannotBeReplaced(bool initialized, bool createResult)
    {
        var operations = new StubAdministrators { Initialized = initialized, CreateResult = createResult };
        var logic = new AdministratorLogic(operations, new StubPasswords());

        Assert.Equal(InitializationResult.AlreadyInitialized, await logic.InitializeAsync("admin", "long password"));
        Assert.Equal(initialized ? 1 : 2, operations.Calls);
    }

    [Fact]
    public async Task AuthenticationUsesNormalizedNameAndRequiresVerifiedPassword()
    {
        var operations = new StubAdministrators { Found = new("Admin", "ADMIN", "hashed") };
        var passwords = new StubPasswords { Verified = true };
        var logic = new AdministratorLogic(operations, passwords);

        Assert.Equal("Admin", await logic.AuthenticateAsync(" admin ", "long password"));
        Assert.Equal("ADMIN", operations.LookupName);
        Assert.Equal("hashed", passwords.VerifiedHash);
        passwords.Verified = false;
        Assert.Null(await logic.AuthenticateAsync("admin", "long password"));
        operations.Found = null;
        passwords.Verified = true;
        Assert.Null(await logic.AuthenticateAsync("missing", "long password"));
        Assert.Null(passwords.VerifiedHash);
    }

    private sealed class StubAdministrators : IAdministratorOperations
    {
        public bool Initialized { get; init; }
        public bool CreateResult { get; init; } = true;
        public int Calls { get; private set; }
        public AdministratorCredentials? Created { get; private set; }
        public AdministratorCredentials? Found { get; set; }
        public string? LookupName { get; private set; }

        public Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Initialized);
        }

        public Task<bool> TryCreateInitialAsync(AdministratorCredentials administrator, CancellationToken cancellationToken = default)
        {
            Calls++;
            Created = administrator;
            return Task.FromResult(CreateResult);
        }

        public Task<AdministratorCredentials?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
        {
            Calls++;
            LookupName = normalizedUserName;
            return Task.FromResult(Found);
        }
    }

    private sealed class StubPasswords : IPasswordOperations
    {
        public bool Verified { get; set; }
        public string? VerifiedHash { get; private set; }
        public string Hash(string password) => "hashed";
        public bool Verify(string? passwordHash, string password)
        {
            VerifiedHash = passwordHash;
            return Verified;
        }
    }
}
