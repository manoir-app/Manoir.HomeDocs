namespace Manoir.HomeDocs.Contracts;

/// <summary>Colors as #rrggbb. Validated by the logic, so they can be used as-is in generated CSS variables.</summary>
public sealed record ThemeColors(
    string Background,
    string Surface,
    string Text,
    string MutedText,
    string Accent,
    string AccentText,
    string Border,
    string Focus);

/// <summary>FontFamily: system, serif, rounded or mono. Shadow: none, soft or strong.</summary>
public sealed record ThemeStyle(string FontFamily, int RadiusPx, string Shadow, int IconSizePx);

/// <summary>ColorScheme is light or dark: it tells the browser which native controls to use.</summary>
public sealed record Theme(
    string Id,
    string Name,
    string ColorScheme,
    ThemeColors Colors,
    ThemeStyle Style,
    bool IsBuiltIn,
    long Revision);

public sealed record ThemeInput(string? Id, string? Name, string? ColorScheme, ThemeColors? Colors, ThemeStyle? Style);

/// <summary>
/// How a profile picks its theme. Mode: system (the light or dark theme follows the device preference),
/// light or dark (that theme is forced).
/// </summary>
public sealed record ThemeSelection(string Mode, string LightThemeId, string DarkThemeId);

/// <summary>Presentation settings of a profile. CardSize: compact, comfortable or large.</summary>
public sealed record ProfileLayout(
    int FontSizePx,
    double TypeScale,
    double SpacingScale,
    int MaxColumns,
    string CardSize,
    string InteractionMode,
    int MinTargetPx,
    int ContentMaxWidthPx,
    bool ShowTouchActions,
    bool ShowPresentation);

/// <summary>Revision 0 and IsCustomized false mean that the profile still uses its built-in defaults.</summary>
public sealed record ProfileSettings(
    string ProfileId,
    ThemeSelection Theme,
    ProfileLayout Layout,
    bool IsCustomized,
    long Revision);

public sealed record ProfileSettingsInput(ThemeSelection? Theme, ProfileLayout? Layout);

public sealed record PublicTheme(string ColorScheme, ThemeColors Colors, ThemeStyle Style);

/// <summary>What the public reader needs to dress a page: the themes for light and dark, and the layout.</summary>
public sealed record PublicAppearance(
    string Profile,
    string ThemeMode,
    PublicTheme Light,
    PublicTheme Dark,
    ProfileLayout Layout);
