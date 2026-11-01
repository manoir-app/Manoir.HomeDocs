using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum InitializationResult
{
    Created,
    AlreadyInitialized,
    InvalidInput
}

public enum AdministratorChangeResult
{
    Updated,
    InvalidInput,
    InvalidCurrentPassword,
    NotFound,
    Conflict
}

public sealed record AdministratorSettings(string UserName, bool PinConfigured, string Language);

public sealed class AdministratorLogic(IAdministratorOperations administrators, IPasswordOperations passwords)
{
    private static readonly Regex PinPattern = new("\\A[0-9]{6}\\z", RegexOptions.CultureInvariant);
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

    public async Task<AdministratorSettings?> GetSettingsAsync(string? userName, CancellationToken cancellationToken = default)
    {
        var credentials = await FindAsync(userName, cancellationToken);
        return credentials is null
            ? null
            : new AdministratorSettings(credentials.UserName, credentials.PinHash is not null, credentials.Language);
    }

    public async Task<AdministratorChangeResult> UpdateCredentialsAsync(
        string? currentUserName,
        string? currentPassword,
        string? newUserName,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        var current = await FindAsync(currentUserName, cancellationToken);
        if (current is null) return AdministratorChangeResult.NotFound;
        if (!passwords.Verify(current.PasswordHash, currentPassword ?? string.Empty))
            return AdministratorChangeResult.InvalidCurrentPassword;

        var name = newUserName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 128 || name.Any(char.IsControl) ||
            (newPassword is { Length: > 0 } && newPassword.Length is < 12 or > 1024))
            return AdministratorChangeResult.InvalidInput;

        var normalizedName = name.ToUpperInvariant();
        if (normalizedName.Length > 128) return AdministratorChangeResult.InvalidInput;
        var passwordHash = string.IsNullOrEmpty(newPassword)
            ? current.PasswordHash
            : passwords.Hash(newPassword);
        var updated = await administrators.UpdateCredentialsAsync(
            current.NormalizedUserName, name, normalizedName, passwordHash, cancellationToken);
        return updated ? AdministratorChangeResult.Updated : AdministratorChangeResult.Conflict;
    }

    public async Task<AdministratorChangeResult> UpdatePinAsync(
        string? currentUserName,
        string? currentPassword,
        string? pin,
        CancellationToken cancellationToken = default)
    {
        var current = await FindAsync(currentUserName, cancellationToken);
        if (current is null) return AdministratorChangeResult.NotFound;
        if (!passwords.Verify(current.PasswordHash, currentPassword ?? string.Empty))
            return AdministratorChangeResult.InvalidCurrentPassword;
        if (pin is not null && !PinPattern.IsMatch(pin))
            return AdministratorChangeResult.InvalidInput;

        var hash = pin is null ? null : passwords.Hash(pin);
        var updated = await administrators.UpdatePinHashAsync(current.NormalizedUserName, hash, cancellationToken);
        return updated ? AdministratorChangeResult.Updated : AdministratorChangeResult.Conflict;
    }

    public async Task<bool> VerifyPinAsync(string? pin, CancellationToken cancellationToken = default)
    {
        if (pin is null || !PinPattern.IsMatch(pin)) return false;
        var pinHash = await administrators.GetPinHashAsync(cancellationToken);
        return pinHash is not null && passwords.Verify(pinHash, pin);
    }

    public async Task<AdministratorChangeResult> UpdateLanguageAsync(
        string? currentUserName,
        string? language,
        CancellationToken cancellationToken = default)
    {
        if (language is not ("fr" or "en")) return AdministratorChangeResult.InvalidInput;
        var current = await FindAsync(currentUserName, cancellationToken);
        if (current is null) return AdministratorChangeResult.NotFound;
        var updated = await administrators.UpdateLanguageAsync(current.NormalizedUserName, language, cancellationToken);
        return updated ? AdministratorChangeResult.Updated : AdministratorChangeResult.Conflict;
    }

    private Task<AdministratorCredentials?> FindAsync(string? userName, CancellationToken cancellationToken)
    {
        var normalizedName = userName?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(normalizedName)
            ? Task.FromResult<AdministratorCredentials?>(null)
            : administrators.FindByNormalizedUserNameAsync(normalizedName, cancellationToken);
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
