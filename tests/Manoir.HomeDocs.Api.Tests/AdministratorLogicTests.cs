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

    [Fact]
    public async Task SettingsExposePinPresenceAndLanguageButNeverHashes()
    {
        var operations = new StubAdministrators { Found = new("Admin", "ADMIN", "password-hash", "pin-hash", "en") };
        var logic = new AdministratorLogic(operations, new StubPasswords());

        Assert.Equal(new AdministratorSettings("Admin", true, "en"), await logic.GetSettingsAsync("admin"));
        operations.Found = new("Admin", "ADMIN", "password-hash", null, "fr");
        Assert.Equal(new AdministratorSettings("Admin", false, "fr"), await logic.GetSettingsAsync("admin"));
        Assert.Null(await logic.GetSettingsAsync("missing"));
    }

    [Fact]
    public async Task CredentialChangesRequireCurrentPasswordAndPreserveUnchangedPassword()
    {
        var operations = new StubAdministrators { Found = new("Admin", "ADMIN", "old-hash") };
        var passwords = new StubPasswords();
        var logic = new AdministratorLogic(operations, passwords);

        Assert.Equal(AdministratorChangeResult.InvalidCurrentPassword,
            await logic.UpdateCredentialsAsync("Admin", "wrong", "New Admin", "a sufficiently long password"));
        Assert.Null(operations.UpdatedUserName);

        passwords.Verified = true;
        Assert.Equal(AdministratorChangeResult.Updated,
            await logic.UpdateCredentialsAsync("Admin", "current", " New Admin ", null));
        Assert.Equal("New Admin", operations.UpdatedUserName);
        Assert.Equal("NEW ADMIN", operations.UpdatedNormalizedUserName);
        Assert.Equal("old-hash", operations.UpdatedPasswordHash);
        Assert.Equal("old-hash", passwords.VerifiedHash);

        Assert.Equal(AdministratorChangeResult.InvalidInput,
            await logic.UpdateCredentialsAsync("Admin", "current", "New Admin", "short"));
        Assert.Equal(AdministratorChangeResult.InvalidInput,
            await logic.UpdateCredentialsAsync("Admin", "current", "invalid\nname", null));
    }

    [Fact]
    public async Task PinMustBeSixDigitsAndOnlyItsHashIsPersisted()
    {
        var operations = new StubAdministrators { Found = new("Admin", "ADMIN", "old-hash") };
        var passwords = new StubPasswords { Verified = true };
        var logic = new AdministratorLogic(operations, passwords);

        Assert.Equal(AdministratorChangeResult.InvalidInput, await logic.UpdatePinAsync("Admin", "current", "12345"));
        Assert.Equal(AdministratorChangeResult.InvalidInput, await logic.UpdatePinAsync("Admin", "current", "123456\n"));
        Assert.Null(operations.UpdatedPinHash);

        Assert.Equal(AdministratorChangeResult.Updated, await logic.UpdatePinAsync("Admin", "current", "012345"));
        Assert.Equal("hashed", operations.UpdatedPinHash);
        Assert.Equal(AdministratorChangeResult.Updated, await logic.UpdatePinAsync("Admin", "current", null));
        Assert.Null(operations.UpdatedPinHash);

        passwords.Verified = false;
        Assert.Equal(AdministratorChangeResult.InvalidCurrentPassword,
            await logic.UpdatePinAsync("Admin", "wrong", "123456"));
    }

    [Fact]
    public async Task LanguageIsRestrictedToSupportedLocales()
    {
        var operations = new StubAdministrators { Found = new("Admin", "ADMIN", "old-hash") };
        var logic = new AdministratorLogic(operations, new StubPasswords());

        Assert.Equal(AdministratorChangeResult.InvalidInput, await logic.UpdateLanguageAsync("Admin", "de"));
        Assert.Null(operations.UpdatedLanguage);
        Assert.Equal(AdministratorChangeResult.Updated, await logic.UpdateLanguageAsync("Admin", "en"));
        Assert.Equal("en", operations.UpdatedLanguage);
    }

    private sealed class StubAdministrators : IAdministratorOperations
    {
        public bool Initialized { get; init; }
        public bool CreateResult { get; init; } = true;
        public int Calls { get; private set; }
        public AdministratorCredentials? Created { get; private set; }
        public AdministratorCredentials? Found { get; set; }
        public string? LookupName { get; private set; }
        public bool UpdateResult { get; init; } = true;
        public string? UpdatedUserName { get; private set; }
        public string? UpdatedNormalizedUserName { get; private set; }
        public string? UpdatedPasswordHash { get; private set; }
        public string? UpdatedPinHash { get; private set; }
        public string? UpdatedLanguage { get; private set; }

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
            return Task.FromResult(Found?.NormalizedUserName == normalizedUserName ? Found : null);
        }

        public Task<bool> UpdateCredentialsAsync(string currentNormalizedUserName, string userName, string normalizedUserName, string passwordHash, CancellationToken cancellationToken = default)
        {
            UpdatedUserName = userName;
            UpdatedNormalizedUserName = normalizedUserName;
            UpdatedPasswordHash = passwordHash;
            Calls++;
            return Task.FromResult(UpdateResult);
        }

        public Task<bool> UpdatePinHashAsync(string normalizedUserName, string? pinHash, CancellationToken cancellationToken = default)
        {
            UpdatedPinHash = pinHash;
            Calls++;
            return Task.FromResult(UpdateResult);
        }

        public Task<bool> UpdateLanguageAsync(string normalizedUserName, string language, CancellationToken cancellationToken = default)
        {
            UpdatedLanguage = language;
            Calls++;
            return Task.FromResult(UpdateResult);
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
