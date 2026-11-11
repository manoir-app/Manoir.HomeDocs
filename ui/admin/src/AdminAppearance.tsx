import { useCallback, useEffect, useRef, useState } from 'react';
import type { CSSProperties, FormEvent } from 'react';
import { Button, Card } from '@manoir-app/core-admin-ui-kit';
import * as api from './adminAppearanceApi';
import type {
  ProfileLayout,
  ProfileSettings,
  Theme,
  ThemeColors,
  ThemeInput,
  ThemeSelection,
} from './adminAppearanceApi';
import { AdminDevices } from './AdminDevices';
import type { AdminRoute } from './adminRoutes';
import type { TranslationKey } from './translations';

type Translate = (key: TranslationKey) => string;

const profileLabels: Record<string, TranslationKey> = {
  desktop: 'profileDesktop',
  mobile: 'profileMobile',
  'tablet-wall': 'profileTabletWall',
  'tablet-dock': 'profileTabletDock',
  tv: 'profileTv',
};

const colorLabels: Record<keyof ThemeColors, TranslationKey> = {
  background: 'colorBackground',
  surface: 'colorSurface',
  text: 'colorText',
  mutedText: 'colorMutedText',
  accent: 'colorAccent',
  accentText: 'colorAccentText',
  border: 'colorBorder',
  focus: 'colorFocus',
};

const fontOptions = ['system', 'serif', 'rounded', 'mono'] as const;
const shadowOptions = ['none', 'soft', 'strong'] as const;
const cardSizes = ['compact', 'comfortable', 'large'] as const;
const interactionModes = ['pointer', 'touch', 'keyboard', 'remote'] as const;

function failureKey(error: api.AppearanceFailure): TranslationKey {
  if (error === 'invalid') return 'appearanceInvalid';
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'notFound') return 'appearanceNotFound';
  if (error === 'conflict') return 'appearanceConflict';
  if (error === 'inUse') return 'themeInUse';
  if (error === 'builtIn') return 'themeBuiltInLocked';
  return 'appearanceUnavailable';
}

function failureOf(reason: unknown): api.AppearanceFailure {
  return reason instanceof api.AppearanceError ? reason.reason : 'unavailable';
}

function themeToInput(theme: Theme): ThemeInput {
  return {
    id: theme.id,
    name: theme.name,
    colorScheme: theme.colorScheme,
    colors: { ...theme.colors },
    style: { ...theme.style },
  };
}

function slugify(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64);
}

function previewStyle(theme: Theme | undefined, layout: ProfileLayout) {
  if (!theme) return undefined;
  return {
    background: theme.colors.background,
    color: theme.colors.text,
    borderRadius: `${theme.style.radiusPx}px`,
    fontSize: `${Math.min(layout.fontSizePx, 28)}px`,
    ['--preview-surface']: theme.colors.surface,
    ['--preview-muted']: theme.colors.mutedText,
    ['--preview-accent']: theme.colors.accent,
    ['--preview-on-accent']: theme.colors.accentText,
    ['--preview-border']: theme.colors.border,
    ['--preview-focus']: theme.colors.focus,
  } as CSSProperties;
}

function ThemePreview({
  t,
  theme,
  layout,
}: {
  t: Translate;
  theme: Theme | undefined;
  layout: ProfileLayout;
}) {
  return (
    <div
      className="appearance-preview"
      style={previewStyle(theme, layout)}
      data-testid="appearance-preview"
    >
      <strong>{t('previewTitle')}</strong>
      <p>{t('previewText')}</p>
      <p className="appearance-preview-muted">{t('previewMuted')}</p>
      <span
        className="appearance-preview-button"
        style={{ minHeight: `${Math.min(layout.minTargetPx, 80)}px` }}
      >
        {t('previewButton')}
      </span>
    </div>
  );
}

export function AdminAppearance({
  translate: t,
  onNavigate,
  onSessionExpired,
}: {
  translate: Translate;
  onNavigate: (route: AdminRoute) => void;
  onSessionExpired: () => void;
}) {
  const [themes, setThemes] = useState<Theme[]>([]);
  const [profiles, setProfiles] = useState<ProfileSettings[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<api.AppearanceFailure>();
  const [notice, setNotice] = useState<TranslationKey>();
  const [busy, setBusy] = useState(false);
  const [profileId, setProfileId] = useState('desktop');
  const [selection, setSelection] = useState<ThemeSelection>();
  const [layout, setLayout] = useState<ProfileLayout>();
  const [themeId, setThemeId] = useState<string>();
  const [draft, setDraft] = useState<ThemeInput>();
  const [isNew, setIsNew] = useState(false);
  const requestId = useRef(0);

  const fail = useCallback(
    (reason: unknown) => {
      const failure = failureOf(reason);
      setError(failure);
      setNotice(undefined);
      if (failure === 'sessionExpired') onSessionExpired();
    },
    [onSessionExpired],
  );

  const load = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    try {
      const [loadedThemes, loadedProfiles] = await Promise.all([
        api.listThemes(),
        api.listProfiles(),
      ]);
      if (id !== requestId.current) return;
      setThemes(loadedThemes);
      setProfiles(loadedProfiles);
      setError(undefined);
    } catch (reason) {
      if (id === requestId.current) fail(reason);
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [fail]);

  useEffect(() => {
    const active = requestId;
    queueMicrotask(() => void load());
    return () => {
      active.current++;
    };
  }, [load]);

  const profile = profiles.find((item) => item.profileId === profileId);
  const shownSelection = selection ?? profile?.theme;
  const shownLayout = layout ?? profile?.layout;

  const chooseProfile = (id: string) => {
    setProfileId(id);
    setSelection(undefined);
    setLayout(undefined);
    setNotice(undefined);
  };

  const replaceProfile = (saved: ProfileSettings) => {
    setProfiles((current) =>
      current.map((item) =>
        item.profileId === saved.profileId ? saved : item,
      ),
    );
    setSelection(undefined);
    setLayout(undefined);
  };

  const saveProfile = async (event: FormEvent) => {
    event.preventDefault();
    if (!profile || !shownSelection || !shownLayout) return;
    setBusy(true);
    try {
      replaceProfile(
        await api.saveProfile(profile, shownSelection, shownLayout),
      );
      setError(undefined);
      setNotice('profileSaved');
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const resetProfile = async () => {
    if (!profile) return;
    setBusy(true);
    try {
      replaceProfile(await api.resetProfile(profile));
      setError(undefined);
      setNotice('profileReset');
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const selectedTheme = themes.find((item) => item.id === themeId);

  const openTheme = (theme: Theme) => {
    setThemeId(theme.id);
    setDraft(themeToInput(theme));
    setIsNew(false);
    setNotice(undefined);
  };

  const startTheme = (from?: Theme) => {
    const base =
      from ?? themes.find((item) => item.id === 'clair') ?? themes[0];
    if (!base) return;
    setThemeId(undefined);
    setDraft({ ...themeToInput(base), id: '', name: '' });
    setIsNew(true);
    setNotice(undefined);
  };

  const saveTheme = async (event: FormEvent) => {
    event.preventDefault();
    if (!draft) return;
    setBusy(true);
    try {
      const input = { ...draft, id: isNew ? slugify(draft.name) : draft.id };
      const saved =
        isNew || !selectedTheme
          ? await api.createTheme(input)
          : await api.updateTheme(selectedTheme, input);
      setThemes(await api.listThemes());
      setThemeId(saved.id);
      setDraft(themeToInput(saved));
      setIsNew(false);
      setError(undefined);
      setNotice('themeSaved');
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const deleteTheme = async () => {
    if (!selectedTheme || !window.confirm(t('themeDeleteConfirm'))) return;
    setBusy(true);
    try {
      await api.deleteTheme(selectedTheme);
      setThemes(await api.listThemes());
      setThemeId(undefined);
      setDraft(undefined);
      setError(undefined);
      setNotice('themeDeleted');
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const setLayoutValue = <K extends keyof ProfileLayout>(
    key: K,
    value: ProfileLayout[K],
  ) => shownLayout && setLayout({ ...shownLayout, [key]: value });

  const numberField = (
    key:
      | 'fontSizePx'
      | 'typeScale'
      | 'spacingScale'
      | 'maxColumns'
      | 'minTargetPx'
      | 'contentMaxWidthPx',
    label: TranslationKey,
    min: number,
    max: number,
    step: number,
  ) =>
    shownLayout && (
      <label className="content-field" htmlFor={`layout-${key}`}>
        <span>{t(label)}</span>
        <input
          id={`layout-${key}`}
          type="number"
          min={min}
          max={max}
          step={step}
          value={shownLayout[key]}
          onChange={(event) => setLayoutValue(key, Number(event.target.value))}
        />
      </label>
    );

  const themeOptions = (scheme: 'light' | 'dark') =>
    themes
      .filter((item) => item.colorScheme === scheme)
      .map((item) => (
        <option key={item.id} value={item.id}>
          {item.name}
        </option>
      ));

  const previewTheme = (() => {
    if (!shownSelection) return undefined;
    const id =
      shownSelection.mode === 'dark'
        ? shownSelection.darkThemeId
        : shownSelection.lightThemeId;
    return themes.find((item) => item.id === id);
  })();
  const previewDarkTheme = shownSelection
    ? themes.find((item) => item.id === shownSelection.darkThemeId)
    : undefined;

  return (
    <main className="mn-kit-theme content-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={() => onNavigate(null)}>
          {t('backToAdmin')}
        </Button>
      </header>
      <section className="appearance-content">
        <div className="content-heading">
          <div>
            <h1>{t('appearanceTitle')}</h1>
            <p>{t('appearanceDescription')}</p>
          </div>
        </div>
        {error && <p role="alert">{t(failureKey(error))}</p>}
        {notice && <p role="status">{t(notice)}</p>}
        {loading && <p role="status">{t('loading')}</p>}

        <Card className="appearance-card">
          <h2>{t('profilesTitle')}</h2>
          <p>{t('profilesDescription')}</p>
          <div
            className="appearance-tabs"
            role="group"
            aria-label={t('profilesTitle')}
          >
            {profiles.map((item) => (
              <Button
                key={item.profileId}
                variant={item.profileId === profileId ? 'primary' : 'secondary'}
                aria-pressed={item.profileId === profileId}
                onClick={() => chooseProfile(item.profileId)}
              >
                {t(profileLabels[item.profileId] ?? 'profileDesktop')}
                {item.isCustomized ? ' •' : ''}
              </Button>
            ))}
          </div>
          {profile && shownSelection && shownLayout && (
            <form className="appearance-form" onSubmit={saveProfile}>
              <p>
                {profile.isCustomized
                  ? t('profileCustomized')
                  : t('profileDefault')}
              </p>
              <div className="appearance-grid">
                <label className="content-field" htmlFor="theme-mode">
                  <span>{t('themeMode')}</span>
                  <select
                    id="theme-mode"
                    value={shownSelection.mode}
                    onChange={(event) =>
                      setSelection({
                        ...shownSelection,
                        mode: event.target.value as ThemeSelection['mode'],
                      })
                    }
                  >
                    <option value="system">{t('modeSystem')}</option>
                    <option value="light">{t('modeLight')}</option>
                    <option value="dark">{t('modeDark')}</option>
                  </select>
                </label>
                <label className="content-field" htmlFor="theme-light">
                  <span>{t('themeLight')}</span>
                  <select
                    id="theme-light"
                    value={shownSelection.lightThemeId}
                    onChange={(event) =>
                      setSelection({
                        ...shownSelection,
                        lightThemeId: event.target.value,
                      })
                    }
                  >
                    {themeOptions('light')}
                  </select>
                </label>
                <label className="content-field" htmlFor="theme-dark">
                  <span>{t('themeDark')}</span>
                  <select
                    id="theme-dark"
                    value={shownSelection.darkThemeId}
                    onChange={(event) =>
                      setSelection({
                        ...shownSelection,
                        darkThemeId: event.target.value,
                      })
                    }
                  >
                    {themeOptions('dark')}
                  </select>
                </label>
                {numberField('fontSizePx', 'layoutFontSize', 12, 48, 1)}
                {numberField('typeScale', 'layoutTypeScale', 0.8, 2, 0.05)}
                {numberField('spacingScale', 'layoutSpacing', 0.5, 2.5, 0.1)}
                {numberField('maxColumns', 'layoutColumns', 1, 6, 1)}
                <label className="content-field" htmlFor="layout-card">
                  <span>{t('layoutCardSize')}</span>
                  <select
                    id="layout-card"
                    value={shownLayout.cardSize}
                    onChange={(event) =>
                      setLayoutValue('cardSize', event.target.value)
                    }
                  >
                    {cardSizes.map((size) => (
                      <option key={size} value={size}>
                        {t(`card_${size}` as TranslationKey)}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="content-field" htmlFor="layout-interaction">
                  <span>{t('layoutInteraction')}</span>
                  <select
                    id="layout-interaction"
                    value={shownLayout.interactionMode}
                    onChange={(event) =>
                      setLayoutValue('interactionMode', event.target.value)
                    }
                  >
                    {interactionModes.map((mode) => (
                      <option key={mode} value={mode}>
                        {t(`interaction_${mode}` as TranslationKey)}
                      </option>
                    ))}
                  </select>
                </label>
                {numberField('minTargetPx', 'layoutMinTarget', 24, 120, 1)}
                {numberField(
                  'contentMaxWidthPx',
                  'layoutContentWidth',
                  480,
                  2400,
                  10,
                )}
              </div>
              <label className="appearance-check">
                <input
                  type="checkbox"
                  checked={shownLayout.showTouchActions}
                  onChange={(event) =>
                    setLayoutValue('showTouchActions', event.target.checked)
                  }
                />
                {t('layoutTouchActions')}
              </label>
              <label className="appearance-check">
                <input
                  type="checkbox"
                  checked={shownLayout.showPresentation}
                  onChange={(event) =>
                    setLayoutValue('showPresentation', event.target.checked)
                  }
                />
                {t('layoutPresentation')}
              </label>
              <div className="appearance-previews">
                <ThemePreview t={t} theme={previewTheme} layout={shownLayout} />
                {shownSelection.mode === 'system' && (
                  <ThemePreview
                    t={t}
                    theme={previewDarkTheme}
                    layout={shownLayout}
                  />
                )}
              </div>
              <div className="content-editor-tools">
                <Button type="submit" disabled={busy}>
                  {t('profileSave')}
                </Button>
                <Button
                  variant="secondary"
                  disabled={busy || !profile.isCustomized}
                  onClick={() => void resetProfile()}
                >
                  {t('profileResetAction')}
                </Button>
              </div>
            </form>
          )}
        </Card>

        <Card className="appearance-card">
          <h2>{t('themesTitle')}</h2>
          <p>{t('themesDescription')}</p>
          <ul className="appearance-themes">
            {themes.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  aria-current={item.id === themeId}
                  onClick={() => openTheme(item)}
                >
                  <span
                    className="appearance-swatch"
                    style={{
                      background: item.colors.background,
                      borderColor: item.colors.accent,
                    }}
                    aria-hidden="true"
                  />
                  {item.name}
                  {item.isBuiltIn && <small>{t('themeBuiltIn')}</small>}
                </button>
              </li>
            ))}
          </ul>
          <Button variant="secondary" onClick={() => startTheme()}>
            {t('themeNew')}
          </Button>
          {draft && (
            <form className="appearance-form" onSubmit={saveTheme}>
              {selectedTheme?.isBuiltIn && (
                <p>
                  {t('themeBuiltInLocked')}{' '}
                  <Button
                    variant="secondary"
                    onClick={() => startTheme(selectedTheme)}
                  >
                    {t('themeDuplicate')}
                  </Button>
                </p>
              )}
              <fieldset disabled={selectedTheme?.isBuiltIn === true || busy}>
                <div className="appearance-grid">
                  <label className="content-field" htmlFor="theme-name">
                    <span>{t('themeName')}</span>
                    <input
                      id="theme-name"
                      maxLength={64}
                      required
                      value={draft.name}
                      onChange={(event) =>
                        setDraft({ ...draft, name: event.target.value })
                      }
                    />
                  </label>
                  <label className="content-field" htmlFor="theme-scheme">
                    <span>{t('themeScheme')}</span>
                    <select
                      id="theme-scheme"
                      value={draft.colorScheme}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          colorScheme: event.target.value as 'light' | 'dark',
                        })
                      }
                    >
                      <option value="light">{t('modeLight')}</option>
                      <option value="dark">{t('modeDark')}</option>
                    </select>
                  </label>
                  {(Object.keys(colorLabels) as (keyof ThemeColors)[]).map(
                    (key) => (
                      <label
                        key={key}
                        className="content-field"
                        htmlFor={`color-${key}`}
                      >
                        <span>{t(colorLabels[key])}</span>
                        <input
                          id={`color-${key}`}
                          type="color"
                          value={draft.colors[key]}
                          onChange={(event) =>
                            setDraft({
                              ...draft,
                              colors: {
                                ...draft.colors,
                                [key]: event.target.value,
                              },
                            })
                          }
                        />
                      </label>
                    ),
                  )}
                  <label className="content-field" htmlFor="theme-font">
                    <span>{t('themeFont')}</span>
                    <select
                      id="theme-font"
                      value={draft.style.fontFamily}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          style: {
                            ...draft.style,
                            fontFamily: event.target.value,
                          },
                        })
                      }
                    >
                      {fontOptions.map((font) => (
                        <option key={font} value={font}>
                          {t(`font_${font}` as TranslationKey)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="content-field" htmlFor="theme-radius">
                    <span>{t('themeRadius')}</span>
                    <input
                      id="theme-radius"
                      type="number"
                      min={0}
                      max={32}
                      value={draft.style.radiusPx}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          style: {
                            ...draft.style,
                            radiusPx: Number(event.target.value),
                          },
                        })
                      }
                    />
                  </label>
                  <label className="content-field" htmlFor="theme-shadow">
                    <span>{t('themeShadow')}</span>
                    <select
                      id="theme-shadow"
                      value={draft.style.shadow}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          style: { ...draft.style, shadow: event.target.value },
                        })
                      }
                    >
                      {shadowOptions.map((shadow) => (
                        <option key={shadow} value={shadow}>
                          {t(`shadow_${shadow}` as TranslationKey)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="content-field" htmlFor="theme-icon">
                    <span>{t('themeIconSize')}</span>
                    <input
                      id="theme-icon"
                      type="number"
                      min={16}
                      max={64}
                      value={draft.style.iconSizePx}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          style: {
                            ...draft.style,
                            iconSizePx: Number(event.target.value),
                          },
                        })
                      }
                    />
                  </label>
                </div>
              </fieldset>
              <ThemePreview
                t={t}
                layout={shownLayout ?? defaultLayout}
                theme={{
                  ...(selectedTheme ?? themes[0]),
                  ...draft,
                  isBuiltIn: false,
                  revision: 0,
                }}
              />
              {!selectedTheme?.isBuiltIn && (
                <div className="content-editor-tools">
                  <Button type="submit" disabled={busy}>
                    {t('themeSave')}
                  </Button>
                  {!isNew && selectedTheme && (
                    <Button
                      variant="secondary"
                      disabled={busy}
                      onClick={() => void deleteTheme()}
                    >
                      {t('themeDelete')}
                    </Button>
                  )}
                </div>
              )}
            </form>
          )}
        </Card>

        <AdminDevices
          translate={t}
          themes={themes}
          onFailure={fail}
          onThemesChanged={() => void load()}
        />
      </section>
    </main>
  );
}

const defaultLayout: ProfileLayout = {
  fontSizePx: 16,
  typeScale: 1,
  spacingScale: 1,
  maxColumns: 3,
  cardSize: 'comfortable',
  interactionMode: 'pointer',
  minTargetPx: 44,
  contentMaxWidthPx: 1000,
  showTouchActions: false,
  showPresentation: true,
};
