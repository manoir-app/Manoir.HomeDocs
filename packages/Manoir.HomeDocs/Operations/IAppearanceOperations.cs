using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Operations;

public enum AppearanceWriteResult
{
    Success,
    NotFound,
    Conflict
}

/// <summary>Custom themes only: the built-in light and dark themes live in the logic.</summary>
public interface IThemeOperations
{
    Task<IReadOnlyList<Theme>> ListAsync(CancellationToken cancellationToken = default);
    Task<Theme?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates the theme (revision must be 1, Conflict if the id exists) or updates it when the revision matches.</summary>
    Task<AppearanceWriteResult> SaveAsync(Theme theme, bool isNew, CancellationToken cancellationToken = default);

    Task<AppearanceWriteResult> DeleteAsync(string id, long revision, CancellationToken cancellationToken = default);
}

/// <summary>Stored overrides of the profile settings. A profile without a row uses its defaults.</summary>
public interface IProfileSettingsOperations
{
    Task<IReadOnlyList<ProfileSettings>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the settings when <paramref name="expectedRevision"/> matches the stored one (0 when none).</summary>
    Task<AppearanceWriteResult> SaveAsync(ProfileSettings settings, long expectedRevision, CancellationToken cancellationToken = default);

    Task<AppearanceWriteResult> ResetAsync(string profileId, CancellationToken cancellationToken = default);
}
