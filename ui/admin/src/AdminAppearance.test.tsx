import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminAppearance } from './AdminAppearance';
import { AppearanceError } from './adminAppearanceApi';
import type { ProfileSettings, Theme } from './adminAppearanceApi';
import { translate } from './translations';
import type { TranslationKey } from './translations';

const mocks = vi.hoisted(() => ({
  listThemes: vi.fn(),
  listProfiles: vi.fn(),
  createTheme: vi.fn(),
  updateTheme: vi.fn(),
  deleteTheme: vi.fn(),
  saveProfile: vi.fn(),
  resetProfile: vi.fn(),
  listDevices: vi.fn(),
  createDevice: vi.fn(),
  updateDevice: vi.fn(),
  deleteDevice: vi.fn(),
}));

vi.mock('./adminAppearanceApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./adminAppearanceApi')>();
  return { ...actual, ...mocks };
});

const t = (key: TranslationKey) => translate('fr', key);

const colors = {
  background: '#faf6ef',
  surface: '#ffffff',
  text: '#2e2a24',
  mutedText: '#6b6358',
  accent: '#2f6f4f',
  accentText: '#ffffff',
  border: '#e3dccf',
  focus: '#b45309',
};
const style = {
  fontFamily: 'system',
  radiusPx: 12,
  shadow: 'soft',
  iconSizePx: 24,
};
const clair: Theme = {
  id: 'clair',
  name: 'Clair',
  colorScheme: 'light',
  colors,
  style,
  isBuiltIn: true,
  revision: 1,
};
const sombre: Theme = {
  ...clair,
  id: 'sombre',
  name: 'Sombre',
  colorScheme: 'dark',
  colors: { ...colors, background: '#1d1b18' },
};
const layout = {
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
const profile = (profileId: string, overrides = {}): ProfileSettings => ({
  profileId,
  theme: { mode: 'system', lightThemeId: 'clair', darkThemeId: 'sombre' },
  layout,
  isCustomized: false,
  revision: 0,
  ...overrides,
});

function renderPage() {
  const onSessionExpired = vi.fn();
  render(
    <AdminAppearance
      translate={t}
      onNavigate={vi.fn()}
      onSessionExpired={onSessionExpired}
    />,
  );
  return { onSessionExpired };
}

describe('apparence', () => {
  beforeEach(() => {
    Object.values(mocks).forEach((mock) => mock.mockReset());
    mocks.listDevices.mockResolvedValue([]);
    mocks.listThemes.mockResolvedValue([clair, sombre]);
    mocks.listProfiles.mockResolvedValue([
      profile('desktop'),
      profile('tv', { layout: { ...layout, fontSizePx: 28 } }),
    ]);
  });
  afterEach(cleanup);

  it('affiche les réglages du profil choisi et enregistre ses modifications', async () => {
    mocks.saveProfile.mockImplementation(
      async (current: ProfileSettings, theme, nextLayout) => ({
        ...current,
        theme,
        layout: nextLayout,
        isCustomized: true,
        revision: 1,
      }),
    );
    renderPage();
    expect(await screen.findByLabelText(t('layoutFontSize'))).toHaveValue(16);

    fireEvent.click(screen.getByRole('button', { name: t('profileTv') }));
    expect(screen.getByLabelText(t('layoutFontSize'))).toHaveValue(28);

    fireEvent.change(screen.getByLabelText(t('layoutFontSize')), {
      target: { value: '32' },
    });
    fireEvent.change(screen.getByLabelText(t('themeMode')), {
      target: { value: 'dark' },
    });
    fireEvent.click(screen.getByRole('button', { name: t('profileSave') }));

    await waitFor(() => expect(mocks.saveProfile).toHaveBeenCalled());
    const [saved, selection, savedLayout] = mocks.saveProfile.mock.calls[0];
    expect(saved.profileId).toBe('tv');
    expect(selection.mode).toBe('dark');
    expect(savedLayout.fontSizePx).toBe(32);
    expect(await screen.findByText(t('profileSaved'))).toBeVisible();
    expect(screen.getByText(t('profileCustomized'))).toBeVisible();
  });

  it('ne permet pas de modifier un thème fourni mais propose de le dupliquer', async () => {
    mocks.createTheme.mockImplementation(async (input) => ({
      ...input,
      isBuiltIn: false,
      revision: 1,
    }));
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: /Clair/ }));
    expect(screen.getByLabelText(t('themeName'))).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: t('themeSave') }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: t('themeDuplicate') }));
    fireEvent.change(screen.getByLabelText(t('themeName')), {
      target: { value: 'Contraste élevé' },
    });
    fireEvent.change(screen.getByLabelText(t('colorAccent')), {
      target: { value: '#ffd400' },
    });
    fireEvent.click(screen.getByRole('button', { name: t('themeSave') }));

    await waitFor(() => expect(mocks.createTheme).toHaveBeenCalled());
    const input = mocks.createTheme.mock.calls[0][0];
    expect(input.id).toBe('contraste-eleve');
    expect(input.colors.accent).toBe('#ffd400');
    expect(await screen.findByText(t('themeSaved'))).toBeVisible();
  });

  it('explique qu’un thème utilisé ne peut pas être supprimé', async () => {
    const custom: Theme = {
      ...clair,
      id: 'perso',
      name: 'Perso',
      isBuiltIn: false,
    };
    mocks.listThemes.mockResolvedValue([clair, sombre, custom]);
    mocks.deleteTheme.mockRejectedValue(new AppearanceError('inUse'));
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: /Perso/ }));
    fireEvent.click(screen.getByRole('button', { name: t('themeDelete') }));
    expect(await screen.findByRole('alert')).toHaveTextContent(t('themeInUse'));
  });

  it('prévient quand la session a expiré', async () => {
    mocks.listThemes.mockRejectedValue(new AppearanceError('sessionExpired'));
    const { onSessionExpired } = renderPage();
    await waitFor(() => expect(onSessionExpired).toHaveBeenCalled());
  });
});

describe('devices', () => {
  it('enregistre un écran avec un override partiel et du CSS', async () => {
    mocks.listThemes.mockResolvedValue([clair, sombre]);
    mocks.listProfiles.mockResolvedValue([profile('tv')]);
    mocks.listDevices.mockResolvedValue([]);
    mocks.createDevice.mockImplementation(async (input) => ({
      ...input,
      lastSeenAt: null,
      revision: 1,
    }));
    renderPage();
    fireEvent.click(
      await screen.findByRole('button', { name: t('deviceNew') }),
    );
    fireEvent.change(screen.getByLabelText(t('deviceName')), {
      target: { value: 'Fire TV cuisine' },
    });
    fireEvent.change(
      screen.getByLabelText(t('layoutFontSize'), {
        selector: '#device-fontSizePx',
      }),
      {
        target: { value: '36' },
      },
    );
    fireEvent.change(screen.getByLabelText(t('deviceCss')), {
      target: { value: '.public-page { letter-spacing: 0.02em; }' },
    });
    fireEvent.click(screen.getByRole('button', { name: t('deviceSave') }));

    await waitFor(() => expect(mocks.createDevice).toHaveBeenCalled());
    const input = mocks.createDevice.mock.calls[0][0];
    expect(input.id).toBe('fire-tv-cuisine');
    expect(input.override.fontSizePx).toBe(36);
    expect(input.override.maxColumns).toBeUndefined();
    expect(input.customCss).toContain('letter-spacing');
    expect(await screen.findByText(t('deviceSaved'))).toBeVisible();
  });
});
