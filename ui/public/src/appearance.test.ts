import { describe, expect, it } from 'vitest';
import { appearanceVariables } from './appearance';
import { parseAppearance } from './publicApi';
import type { PublicAppearance, PublicTheme } from './publicApi';

const light: PublicTheme = {
  colorScheme: 'light',
  colors: {
    background: '#faf6ef',
    surface: '#ffffff',
    text: '#2e2a24',
    mutedText: '#6b6358',
    accent: '#2f6f4f',
    accentText: '#ffffff',
    border: '#e3dccf',
    focus: '#b45309',
  },
  style: { fontFamily: 'system', radiusPx: 12, shadow: 'soft', iconSizePx: 24 },
};
const dark: PublicTheme = {
  colorScheme: 'dark',
  colors: { ...light.colors, background: '#1d1b18', text: '#f3eee6' },
  style: { fontFamily: 'mono', radiusPx: 0, shadow: 'none', iconSizePx: 32 },
};
const appearance: PublicAppearance = {
  profile: 'tv',
  themeMode: 'system',
  light,
  dark,
  layout: {
    fontSizePx: 24,
    typeScale: 1.25,
    spacingScale: 1.5,
    maxColumns: 3,
    cardSize: 'large',
    interactionMode: 'remote',
    minTargetPx: 64,
    contentMaxWidthPx: 1200,
    showTouchActions: false,
    showPresentation: true,
  },
};

describe('appearanceVariables', () => {
  it('follows the system colour preference in system mode', () => {
    expect(appearanceVariables(appearance, false)['--hd-bg']).toBe('#faf6ef');
    const night = appearanceVariables(appearance, true);
    expect(night['--hd-bg']).toBe('#1d1b18');
    expect(night.colorScheme).toBe('dark');
    expect(night['--hd-shadow']).toBe('none');
    expect(night['--hd-font']).toContain('monospace');
  });

  it('ignores the system preference when the profile forces a mode', () => {
    expect(
      appearanceVariables({ ...appearance, themeMode: 'light' }, true)[
        '--hd-bg'
      ],
    ).toBe('#faf6ef');
    expect(
      appearanceVariables({ ...appearance, themeMode: 'dark' }, false)[
        '--hd-bg'
      ],
    ).toBe('#1d1b18');
  });

  it('turns the layout into scalable CSS values', () => {
    const variables = appearanceVariables(appearance, false);
    expect(variables['--hd-base-size']).toBe('24px');
    expect(variables['--hd-type-scale']).toBe('1.25');
    expect(variables['--hd-min-target']).toBe('4rem');
    expect(variables['--hd-content-width']).toBe('75rem');
    expect(variables['--hd-radius']).toBe('0.75rem');
    expect(variables['--hd-card-min-width']).toBe('22rem');
  });
});

describe('parseAppearance', () => {
  it('accepts a well-formed response', () => {
    expect(parseAppearance(structuredClone(appearance)).profile).toBe('tv');
  });

  it('rejects colours that are not #rrggbb, so nothing unsafe reaches the CSS', () => {
    const bad = structuredClone(appearance);
    bad.light.colors.accent = 'red; background: url(x)';
    expect(() => parseAppearance(bad)).toThrow();
    expect(() => parseAppearance({})).toThrow();
    expect(() =>
      parseAppearance({ ...appearance, themeMode: 'auto' }),
    ).toThrow();
  });
});
