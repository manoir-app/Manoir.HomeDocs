using System.Text.RegularExpressions;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum AppearanceResult
{
    Success,
    InvalidInput,
    NotFound,
    Conflict,
    BuiltIn,
    InUse
}

public sealed partial class AppearanceLogic(
    IThemeOperations themes,
    IProfileSettingsOperations profiles,
    IDeviceOperations devices,
    UsageContextLogic contexts)
{
    public const string LightThemeId = "clair";
    public const string DarkThemeId = "sombre";

    public static readonly IReadOnlyList<string> ProfileIds = ["desktop", "mobile", "tablet-wall", "tablet-dock", "tv"];

    private static readonly string[] FontFamilies = ["system", "serif", "rounded", "mono"];
    private static readonly string[] Shadows = ["none", "soft", "strong"];
    private static readonly string[] CardSizes = ["compact", "comfortable", "large"];
    private static readonly string[] InteractionModes = ["pointer", "touch", "keyboard", "remote"];
    private static readonly string[] ThemeModes = ["system", "light", "dark"];

    public static readonly IReadOnlyList<Theme> BuiltInThemes =
    [
        new(LightThemeId, "Clair", "light",
            new ThemeColors("#faf6ef", "#ffffff", "#2e2a24", "#6b6358", "#2f6f4f", "#ffffff", "#e3dccf", "#b45309"),
            new ThemeStyle("system", 12, "soft", 24), true, 1),
        new(DarkThemeId, "Sombre", "dark",
            new ThemeColors("#1d1b18", "#27241f", "#f3eee6", "#b8ad9c", "#7cc79d", "#12261b", "#3a352d", "#f59e0b"),
            new ThemeStyle("system", 12, "soft", 24), true, 1)
    ];

    private static ThemeSelection SystemSelection => new("system", LightThemeId, DarkThemeId);

    private static readonly IReadOnlyDictionary<string, ProfileSettings> Defaults = new Dictionary<string, ProfileSettings>
    {
        ["desktop"] = Default("desktop", SystemSelection, new(17, 1.0, 1.0, 3, "comfortable", "pointer", 44, 1100, false, true)),
        ["mobile"] = Default("mobile", SystemSelection, new(17, 1.0, 1.0, 1, "compact", "touch", 48, 720, true, true)),
        ["tablet-wall"] = Default("tablet-wall", SystemSelection, new(20, 1.1, 1.2, 3, "large", "touch", 64, 1400, true, true)),
        ["tablet-dock"] = Default("tablet-dock", SystemSelection, new(20, 1.1, 1.1, 2, "large", "touch", 56, 1000, true, true)),
        ["tv"] = Default("tv", new("dark", LightThemeId, DarkThemeId), new(28, 1.2, 1.4, 3, "large", "remote", 72, 1600, false, true))
    };

    private static ProfileSettings Default(string id, ThemeSelection theme, ProfileLayout layout) =>
        new(id, theme, layout, false, 0);

    // ---- Themes ----

    public async Task<IReadOnlyList<Theme>> ListThemesAsync(CancellationToken cancellationToken = default) =>
        [.. BuiltInThemes, .. (await themes.ListAsync(cancellationToken)).OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)];

    public async Task<Theme?> FindThemeAsync(string id, CancellationToken cancellationToken = default) =>
        BuiltInThemes.FirstOrDefault(theme => theme.Id == id) ?? await themes.FindAsync(id, cancellationToken);

    public async Task<AppearanceResult> CreateThemeAsync(ThemeInput input, CancellationToken cancellationToken = default)
    {
        if (!TryBuildTheme(input, 1, out var theme)) return AppearanceResult.InvalidInput;
        if (BuiltInThemes.Any(item => item.Id == theme!.Id)) return AppearanceResult.BuiltIn;
        return ToResult(await themes.SaveAsync(theme!, true, cancellationToken));
    }

    public async Task<AppearanceResult> UpdateThemeAsync(string id, long revision, ThemeInput input, CancellationToken cancellationToken = default)
    {
        if (BuiltInThemes.Any(item => item.Id == id)) return AppearanceResult.BuiltIn;
        if (!TryBuildTheme(input with { Id = id }, revision, out var theme)) return AppearanceResult.InvalidInput;
        return ToResult(await themes.SaveAsync(theme!, false, cancellationToken));
    }

    public async Task<AppearanceResult> DeleteThemeAsync(string id, long revision, CancellationToken cancellationToken = default)
    {
        if (BuiltInThemes.Any(item => item.Id == id)) return AppearanceResult.BuiltIn;
        var used = (await ListProfilesAsync(cancellationToken)).Any(profile =>
                       profile.Theme.LightThemeId == id || profile.Theme.DarkThemeId == id) ||
                   (await devices.ListAsync(cancellationToken)).Any(device =>
                       device.Override.LightThemeId == id || device.Override.DarkThemeId == id);
        if (used) return AppearanceResult.InUse;
        return ToResult(await themes.DeleteAsync(id, revision, cancellationToken));
    }

    private static bool TryBuildTheme(ThemeInput input, long revision, out Theme? theme)
    {
        theme = null;
        var id = input.Id?.Trim();
        var name = input.Name?.Trim();
        if (id is null || !ContentPageValidation.IsSlug(id) || id.Length > 64 ||
            name is null || name.Length is 0 or > 64 || name.Any(char.IsControl) ||
            input.ColorScheme is not ("light" or "dark") || input.Colors is null || input.Style is null)
            return false;
        var colors = input.Colors;
        if (!new[] { colors.Background, colors.Surface, colors.Text, colors.MutedText, colors.Accent, colors.AccentText, colors.Border, colors.Focus }
                .All(color => color is not null && HexColorPattern().IsMatch(color)))
            return false;
        var style = input.Style;
        if (!FontFamilies.Contains(style.FontFamily) || !Shadows.Contains(style.Shadow) ||
            style.RadiusPx is < 0 or > 32 || style.IconSizePx is < 16 or > 64)
            return false;
        theme = new Theme(id, name, input.ColorScheme, colors with
        {
            Background = colors.Background.ToLowerInvariant(),
            Surface = colors.Surface.ToLowerInvariant(),
            Text = colors.Text.ToLowerInvariant(),
            MutedText = colors.MutedText.ToLowerInvariant(),
            Accent = colors.Accent.ToLowerInvariant(),
            AccentText = colors.AccentText.ToLowerInvariant(),
            Border = colors.Border.ToLowerInvariant(),
            Focus = colors.Focus.ToLowerInvariant()
        }, style, false, revision);
        return true;
    }

    // ---- Profiles ----

    public static bool IsProfile(string? profileId) => profileId is not null && ProfileIds.Contains(profileId);

    public async Task<IReadOnlyList<ProfileSettings>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var stored = (await profiles.ListAsync(cancellationToken)).ToDictionary(item => item.ProfileId);
        return ProfileIds.Select(id => stored.TryGetValue(id, out var row) ? row with { IsCustomized = true } : Defaults[id]).ToList();
    }

    public async Task<ProfileSettings?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
        IsProfile(profileId) ? (await ListProfilesAsync(cancellationToken)).Single(item => item.ProfileId == profileId) : null;

    public async Task<AppearanceResult> SaveProfileAsync(
        string profileId,
        long revision,
        ProfileSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        if (!IsProfile(profileId)) return AppearanceResult.NotFound;
        if (input.Theme is null || input.Layout is null || !IsValidLayout(input.Layout) || !ThemeModes.Contains(input.Theme.Mode))
            return AppearanceResult.InvalidInput;
        if (await FindThemeAsync(input.Theme.LightThemeId ?? string.Empty, cancellationToken) is null ||
            await FindThemeAsync(input.Theme.DarkThemeId ?? string.Empty, cancellationToken) is null)
            return AppearanceResult.InvalidInput;
        var settings = new ProfileSettings(profileId, input.Theme, input.Layout, true, revision + 1);
        return ToResult(await profiles.SaveAsync(settings, revision, cancellationToken));
    }

    public async Task<AppearanceResult> ResetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
        IsProfile(profileId) ? ToResult(await profiles.ResetAsync(profileId, cancellationToken)) : AppearanceResult.NotFound;

    private static bool IsValidLayout(ProfileLayout layout) =>
        layout.FontSizePx is >= 12 and <= 48 &&
        layout.TypeScale is >= 0.8 and <= 2.0 &&
        layout.SpacingScale is >= 0.5 and <= 2.5 &&
        layout.MaxColumns is >= 1 and <= 6 &&
        CardSizes.Contains(layout.CardSize) &&
        InteractionModes.Contains(layout.InteractionMode) &&
        layout.MinTargetPx is >= 24 and <= 120 &&
        layout.ContentMaxWidthPx is >= 480 and <= 2400;

    // ---- Public ----

    /// <summary>Null when the invocation (context, profile, device) is unknown.</summary>
    public async Task<PublicAppearance?> ResolveAsync(
        string? contextId,
        string? profile,
        string? device,
        CancellationToken cancellationToken = default)
    {
        if (!await contexts.IsKnownInvocationAsync(contextId, profile, device, cancellationToken)) return null;
        var settings = await GetProfileAsync(profile!, cancellationToken);
        if (settings is null) return null;
        var theme = settings.Theme;
        var layout = settings.Layout;
        string? css = null;
        var registered = device is null ? null : await devices.FindAsync(device, cancellationToken);
        if (registered is { IsActive: true })
        {
            (theme, layout) = ApplyOverride(theme, layout, registered.Override);
            css = registered.CustomCss.Length > 0 ? registered.CustomCss : null;
            await devices.TouchAsync(registered.Id, DateTimeOffset.UtcNow, cancellationToken);
        }

        var light = await FindThemeAsync(theme.LightThemeId, cancellationToken) ?? BuiltInThemes[0];
        var dark = await FindThemeAsync(theme.DarkThemeId, cancellationToken) ?? BuiltInThemes[1];
        var (shownLight, shownDark) = theme.Mode switch
        {
            "light" => (light, light),
            "dark" => (dark, dark),
            _ => (light, dark)
        };
        return new PublicAppearance(settings.ProfileId, theme.Mode, ToPublic(shownLight), ToPublic(shownDark), layout, css);
    }

    // ---- Devices ----

    public const int MaxCustomCssLength = 20_000;

    public async Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default) =>
        await devices.ListAsync(cancellationToken);

    public async Task<Device?> FindDeviceAsync(string id, CancellationToken cancellationToken = default) =>
        await devices.FindAsync(id, cancellationToken);

    public async Task<AppearanceResult> CreateDeviceAsync(DeviceInput input, CancellationToken cancellationToken = default)
    {
        if (await TryBuildDeviceAsync(input, 1, cancellationToken) is not { } device) return AppearanceResult.InvalidInput;
        return ToResult(await devices.SaveAsync(device, true, cancellationToken));
    }

    public async Task<AppearanceResult> UpdateDeviceAsync(string id, long revision, DeviceInput input, CancellationToken cancellationToken = default)
    {
        if (await TryBuildDeviceAsync(input with { Id = id }, revision, cancellationToken) is not { } device)
            return AppearanceResult.InvalidInput;
        return ToResult(await devices.SaveAsync(device, false, cancellationToken));
    }

    public async Task<AppearanceResult> DeleteDeviceAsync(string id, long revision, CancellationToken cancellationToken = default) =>
        ToResult(await devices.DeleteAsync(id, revision, cancellationToken));

    private async Task<Device?> TryBuildDeviceAsync(DeviceInput input, long revision, CancellationToken cancellationToken)
    {
        var id = input.Id?.Trim();
        var name = input.Name?.Trim();
        var css = input.CustomCss ?? string.Empty;
        var over = input.Override ?? new DeviceOverride();
        if (id is null || id.Length is 0 or > 128 ||
            !id.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_') ||
            name is null || name.Length is 0 or > 128 || name.Any(char.IsControl) ||
            !IsValidCustomCss(css) || !IsValidOverride(over))
            return null;
        foreach (var themeId in new[] { over.LightThemeId, over.DarkThemeId })
            if (themeId is not null && await FindThemeAsync(themeId, cancellationToken) is null)
                return null;
        return new Device(id, name, input.IsActive, null, over, css, revision);
    }

    private static bool IsValidCustomCss(string css) =>
        css.Length <= MaxCustomCssLength &&
        !css.Contains("</", StringComparison.Ordinal) &&
        !css.Contains("@import", StringComparison.OrdinalIgnoreCase) &&
        !css.Contains('\0');

    private static bool IsValidOverride(DeviceOverride value) =>
        (value.Mode is null || ThemeModes.Contains(value.Mode)) &&
        (value.FontSizePx is null or >= 12 and <= 48) &&
        (value.TypeScale is null or >= 0.8 and <= 2.0) &&
        (value.SpacingScale is null or >= 0.5 and <= 2.5) &&
        (value.MaxColumns is null or >= 1 and <= 6) &&
        (value.CardSize is null || CardSizes.Contains(value.CardSize)) &&
        (value.InteractionMode is null || InteractionModes.Contains(value.InteractionMode)) &&
        (value.MinTargetPx is null or >= 24 and <= 120) &&
        (value.ContentMaxWidthPx is null or >= 480 and <= 2400);

    private static (ThemeSelection, ProfileLayout) ApplyOverride(ThemeSelection theme, ProfileLayout layout, DeviceOverride over) =>
    (
        new ThemeSelection(over.Mode ?? theme.Mode, over.LightThemeId ?? theme.LightThemeId, over.DarkThemeId ?? theme.DarkThemeId),
        layout with
        {
            FontSizePx = over.FontSizePx ?? layout.FontSizePx,
            TypeScale = over.TypeScale ?? layout.TypeScale,
            SpacingScale = over.SpacingScale ?? layout.SpacingScale,
            MaxColumns = over.MaxColumns ?? layout.MaxColumns,
            CardSize = over.CardSize ?? layout.CardSize,
            InteractionMode = over.InteractionMode ?? layout.InteractionMode,
            MinTargetPx = over.MinTargetPx ?? layout.MinTargetPx,
            ContentMaxWidthPx = over.ContentMaxWidthPx ?? layout.ContentMaxWidthPx,
            ShowTouchActions = over.ShowTouchActions ?? layout.ShowTouchActions,
            ShowPresentation = over.ShowPresentation ?? layout.ShowPresentation
        }
    );

    private static PublicTheme ToPublic(Theme theme) => new(theme.ColorScheme, theme.Colors, theme.Style);

    private static AppearanceResult ToResult(AppearanceWriteResult result) => result switch
    {
        AppearanceWriteResult.Success => AppearanceResult.Success,
        AppearanceWriteResult.NotFound => AppearanceResult.NotFound,
        _ => AppearanceResult.Conflict
    };

    [GeneratedRegex("\\A#[0-9a-fA-F]{6}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorPattern();
}
