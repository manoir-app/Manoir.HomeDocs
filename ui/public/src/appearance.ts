import { useEffect, useState } from 'react';
import * as api from './publicApi';
import type { PublicAppearance, PublicInvocation } from './publicApi';

const fonts: Record<string, string> = {
  system: "'Segoe UI', system-ui, sans-serif",
  serif: "Georgia, 'Times New Roman', serif",
  rounded: "ui-rounded, 'Nunito', 'Segoe UI', system-ui, sans-serif",
  mono: "ui-monospace, 'Cascadia Mono', Consolas, monospace",
};

const shadows: Record<string, (color: string) => string> = {
  none: () => 'none',
  soft: (color) => `0 1rem 3rem ${color}`,
  strong: (color) => `0 0.5rem 1.5rem ${color}, 0 0.1rem 0.4rem ${color}`,
};

const cardMinWidths: Record<string, string> = {
  compact: '12rem',
  comfortable: '16rem',
  large: '22rem',
};

export function useDarkPreference(): boolean {
  const [dark, setDark] = useState(
    () =>
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches,
  );
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const query = window.matchMedia('(prefers-color-scheme: dark)');
    const update = () => setDark(query.matches);
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);
  return dark;
}

/** CSS custom properties for the theme chosen for the current colour preference. */
export function appearanceVariables(
  appearance: PublicAppearance,
  prefersDark: boolean,
): Record<string, string> {
  const theme =
    appearance.themeMode === 'dark' ||
    (appearance.themeMode === 'system' && prefersDark)
      ? appearance.dark
      : appearance.light;
  const { colors, style } = theme;
  const { layout } = appearance;
  const shadowColor = `color-mix(in srgb, ${colors.text} ${
    theme.colorScheme === 'dark' ? 50 : 10
  }%, transparent)`;
  return {
    '--hd-bg': colors.background,
    '--hd-surface': colors.surface,
    '--hd-text': colors.text,
    '--hd-muted': colors.mutedText,
    '--hd-accent': colors.accent,
    '--hd-accent-text': `color-mix(in srgb, ${colors.accent} 70%, ${colors.text})`,
    '--hd-on-accent': colors.accentText,
    '--hd-border': colors.border,
    '--hd-focus': colors.focus,
    '--hd-shadow-color': shadowColor,
    '--hd-shadow': (shadows[style.shadow] ?? shadows.soft)(shadowColor),
    '--hd-radius': `${style.radiusPx / 16}rem`,
    '--hd-font': fonts[style.fontFamily] ?? fonts.system,
    '--hd-icon-size': `${style.iconSizePx}px`,
    '--hd-base-size': `${layout.fontSizePx}px`,
    '--hd-type-scale': String(layout.typeScale),
    '--hd-spacing': String(layout.spacingScale),
    '--hd-min-target': `${layout.minTargetPx / 16}rem`,
    '--hd-content-width': `${layout.contentMaxWidthPx / 16}rem`,
    '--hd-max-columns': String(layout.maxColumns),
    '--hd-card-min-width':
      cardMinWidths[layout.cardSize] ?? cardMinWidths.comfortable,
    colorScheme: theme.colorScheme,
  };
}

/**
 * Loads the appearance of the current invocation and applies it to the document.
 * A failure keeps the built-in look: the manual must stay readable without it.
 */
export function useAppearance(invocation: PublicInvocation): void {
  const [appearance, setAppearance] = useState<PublicAppearance>();
  const prefersDark = useDarkPreference();
  const { context, profile, device } = invocation;

  useEffect(() => {
    let active = true;
    queueMicrotask(() => {
      if (!active) return;
      api
        .getAppearance({ context, profile, device })
        .then((value) => {
          if (active) setAppearance(value);
        })
        .catch(() => {
          if (active) setAppearance(undefined);
        });
    });
    return () => {
      active = false;
    };
  }, [context, profile, device]);

  useEffect(() => {
    if (!appearance) return;
    const style = document.documentElement.style;
    const variables = appearanceVariables(appearance, prefersDark);
    for (const [name, value] of Object.entries(variables)) {
      if (name === 'colorScheme') style.colorScheme = value;
      else style.setProperty(name, value);
    }
    document.documentElement.dataset.appearance = appearance.profile;
    return () => {
      for (const name of Object.keys(variables)) {
        if (name === 'colorScheme') style.removeProperty('color-scheme');
        else style.removeProperty(name);
      }
      delete document.documentElement.dataset.appearance;
    };
  }, [appearance, prefersDark]);
}
