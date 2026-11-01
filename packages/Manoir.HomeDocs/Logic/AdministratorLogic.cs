using System.Diagnostics.CodeAnalysis;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum InitializationResult
{
    Created,
    AlreadyInitialized,
    InvalidInput
}

public sealed class AdministratorLogic(IAdministratorOperations administrators, IPasswordOperations passwords)
{
    public async Task<InitializationResult> InitializeAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(userName, password, out var name, out var normalizedName))
        {
            return InitializationResult.InvalidInput;
        }

        if (await administrators.IsInitializedAsync(cancellationToken))
        {
            return InitializationResult.AlreadyInitialized;
        }

        var credentials = new AdministratorCredentials(name, normalizedName, passwords.Hash(password));
        return await administrators.TryCreateInitialAsync(credentials, cancellationToken)
            ? InitializationResult.Created
            : InitializationResult.AlreadyInitialized;
    }

    public async Task<string?> AuthenticateAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(userName, password, out _, out var normalizedName))
        {
            return null;
        }

        var credentials = await administrators.FindByNormalizedUserNameAsync(normalizedName, cancellationToken);
        return passwords.Verify(credentials?.PasswordHash, password) ? credentials?.UserName : null;
    }

    private static bool TryNormalize(string? userName, [NotNullWhen(true)] string? password, out string name, out string normalizedName)
    {
        name = userName?.Trim() ?? string.Empty;
        normalizedName = name.ToUpperInvariant();
        return name.Length is > 0 and <= 128 &&
            normalizedName.Length <= 128 &&
            !name.Any(char.IsControl) &&
            password is { Length: >= 12 and <= 1024 };
    }
}
