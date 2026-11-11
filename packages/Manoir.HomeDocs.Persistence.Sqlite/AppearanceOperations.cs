using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed record ThemeData(ThemeColors Colors, ThemeStyle Style);

internal sealed record ProfileData(ThemeSelection Theme, ProfileLayout Layout);

internal sealed class ThemeOperations(HomeDocsDbContext context) : IThemeOperations
{
    public async Task<IReadOnlyList<Theme>> ListAsync(CancellationToken cancellationToken = default) =>
        (await context.Themes.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken)).Select(ToTheme).ToList();

    public async Task<Theme?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await context.Themes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : ToTheme(row);
    }

    public async Task<AppearanceWriteResult> SaveAsync(Theme theme, bool isNew, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new ThemeData(theme.Colors, theme.Style));
        var current = await context.Themes.SingleOrDefaultAsync(item => item.Id == theme.Id, cancellationToken);
        if (isNew)
        {
            if (current is not null) return AppearanceWriteResult.Conflict;
            context.Themes.Add(new ThemeEntity
            {
                Id = theme.Id, Name = theme.Name, ColorScheme = theme.ColorScheme, DataJson = json, Revision = 1
            });
        }
        else
        {
            if (current is null) return AppearanceWriteResult.NotFound;
            if (current.Revision != theme.Revision) return AppearanceWriteResult.Conflict;
            current.Name = theme.Name;
            current.ColorScheme = theme.ColorScheme;
            current.DataJson = json;
            current.Revision++;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppearanceWriteResult.Conflict;
        }

        return AppearanceWriteResult.Success;
    }

    public async Task<AppearanceWriteResult> DeleteAsync(string id, long revision, CancellationToken cancellationToken = default)
    {
        var current = await context.Themes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (current is null) return AppearanceWriteResult.NotFound;
        if (current.Revision != revision) return AppearanceWriteResult.Conflict;
        context.Themes.Remove(current);
        await context.SaveChangesAsync(cancellationToken);
        return AppearanceWriteResult.Success;
    }

    private static Theme ToTheme(ThemeEntity row)
    {
        var data = JsonSerializer.Deserialize<ThemeData>(row.DataJson)
            ?? throw new InvalidOperationException($"Theme {row.Id} is unreadable.");
        return new Theme(row.Id, row.Name, row.ColorScheme, data.Colors, data.Style, false, row.Revision);
    }
}

internal sealed class ProfileSettingsOperations(HomeDocsDbContext context) : IProfileSettingsOperations
{
    public async Task<IReadOnlyList<ProfileSettings>> ListAsync(CancellationToken cancellationToken = default) =>
        (await context.ProfileSettings.AsNoTracking().OrderBy(item => item.ProfileId).ToListAsync(cancellationToken))
        .Select(row =>
        {
            var data = JsonSerializer.Deserialize<ProfileData>(row.DataJson)
                ?? throw new InvalidOperationException($"Profile {row.ProfileId} is unreadable.");
            return new ProfileSettings(row.ProfileId, data.Theme, data.Layout, true, row.Revision);
        }).ToList();

    public async Task<AppearanceWriteResult> SaveAsync(ProfileSettings settings, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new ProfileData(settings.Theme, settings.Layout));
        var current = await context.ProfileSettings.SingleOrDefaultAsync(item => item.ProfileId == settings.ProfileId, cancellationToken);
        if (current is null)
        {
            if (expectedRevision != 0) return AppearanceWriteResult.Conflict;
            context.ProfileSettings.Add(new ProfileSettingsEntity { ProfileId = settings.ProfileId, DataJson = json, Revision = 1 });
        }
        else
        {
            if (current.Revision != expectedRevision) return AppearanceWriteResult.Conflict;
            current.DataJson = json;
            current.Revision++;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return AppearanceWriteResult.Conflict;
        }

        return AppearanceWriteResult.Success;
    }

    public async Task<AppearanceWriteResult> ResetAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var current = await context.ProfileSettings.SingleOrDefaultAsync(item => item.ProfileId == profileId, cancellationToken);
        if (current is null) return AppearanceWriteResult.Success;
        context.ProfileSettings.Remove(current);
        await context.SaveChangesAsync(cancellationToken);
        return AppearanceWriteResult.Success;
    }
}

internal sealed class DeviceOperations(HomeDocsDbContext context) : IDeviceOperations
{
    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default) =>
        (await context.Devices.AsNoTracking().OrderBy(item => item.Name).ThenBy(item => item.Id).ToListAsync(cancellationToken))
        .Select(ToDevice).ToList();

    public async Task<Device?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await context.Devices.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : ToDevice(row);
    }

    public async Task<AppearanceWriteResult> SaveAsync(Device device, bool isNew, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(device.Override);
        var current = await context.Devices.SingleOrDefaultAsync(item => item.Id == device.Id, cancellationToken);
        if (isNew)
        {
            if (current is not null) return AppearanceWriteResult.Conflict;
            context.Devices.Add(new DeviceEntity
            {
                Id = device.Id, Name = device.Name, IsActive = device.IsActive,
                OverrideJson = json, CustomCss = device.CustomCss, Revision = 1
            });
        }
        else
        {
            if (current is null) return AppearanceWriteResult.NotFound;
            if (current.Revision != device.Revision) return AppearanceWriteResult.Conflict;
            current.Name = device.Name;
            current.IsActive = device.IsActive;
            current.OverrideJson = json;
            current.CustomCss = device.CustomCss;
            current.Revision++;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return AppearanceWriteResult.Conflict;
        }

        return AppearanceWriteResult.Success;
    }

    public async Task<AppearanceWriteResult> DeleteAsync(string id, long revision, CancellationToken cancellationToken = default)
    {
        var current = await context.Devices.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (current is null) return AppearanceWriteResult.NotFound;
        if (current.Revision != revision) return AppearanceWriteResult.Conflict;
        context.Devices.Remove(current);
        await context.SaveChangesAsync(cancellationToken);
        return AppearanceWriteResult.Success;
    }

    public async Task TouchAsync(string id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Only a timestamp, written at most once a minute: a screen refreshing every second must not hammer SQLite.
        var threshold = now.AddMinutes(-1).ToString("O");
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Devices\" SET \"LastSeenAt\" = {now.ToString("O")} WHERE \"Id\" = {id} AND (\"LastSeenAt\" IS NULL OR \"LastSeenAt\" < {threshold})",
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Best effort: the appearance must be served even when this write fails.
        }
    }

    private static Device ToDevice(DeviceEntity row) => new(
        row.Id, row.Name, row.IsActive, row.LastSeenAt,
        JsonSerializer.Deserialize<DeviceOverride>(row.OverrideJson) ?? new DeviceOverride(),
        row.CustomCss, row.Revision);
}
