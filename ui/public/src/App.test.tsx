import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { PublicApiError } from './publicApi';

const mocks = vi.hoisted(() => ({
  getNavigation: vi.fn(),
  getPage: vi.fn(),
  verifyPin: vi.fn(),
  refreshProtectedAccess: vi.fn(),
  logoutProtectedAccess: vi.fn(),
  searchPages: vi.fn(),
  getAppearance: vi.fn(),
}));

vi.mock('./publicApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./publicApi')>();
  return {
    ...actual,
    getNavigation: mocks.getNavigation,
    getPage: mocks.getPage,
    verifyPin: mocks.verifyPin,
    refreshProtectedAccess: mocks.refreshProtectedAccess,
    logoutProtectedAccess: mocks.logoutProtectedAccess,
    searchPages: mocks.searchPages,
    getAppearance: mocks.getAppearance,
  };
});

const invocation = {
  context: 'habitant',
  profile: 'desktop',
  device: 'ordinateur-bureau',
};

const root = {
  nodeId: 'root-node',
  pageId: 'root-page',
  title: 'Accueil',
  slug: 'accueil',
  isLink: false,
  requiresAuthentication: false,
  children: [],
};

const navigation = {
  contextId: 'habitant',
  contextName: 'Habitants',
  profile: 'desktop' as const,
  device: invocation.device,
  root,
  children: root.children,
};

function page(slug: string, markdown = 'Contenu consultable.') {
  return {
    id: `${slug}-id`,
    title: slug === 'accueil' ? 'Accueil' : 'Consignes',
    slug,
    category: 'maison',
    contexts: ['habitant'],
    markdown,
    requiresAuthentication: false,
  };
}

function setUrl(path: string) {
  window.history.replaceState(
    null,
    '',
    `${path}?context=habitant&profile=desktop&device=ordinateur-bureau`,
  );
}

describe('lecteur public', () => {
  beforeEach(() => {
    mocks.getAppearance.mockReset();
    mocks.getAppearance.mockRejectedValue(new PublicApiError('notFound'));
    mocks.getNavigation.mockReset();
    mocks.getPage.mockReset();
    mocks.verifyPin.mockReset();
    mocks.refreshProtectedAccess.mockReset();
    mocks.logoutProtectedAccess.mockReset();
    mocks.searchPages.mockReset();
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    window.history.replaceState(null, '', '/');
  });

  it('affiche une page directe dans un contexte sans racine', async () => {
    setUrl('/consignes');
    mocks.getNavigation.mockRejectedValue(new PublicApiError('notFound'));
    mocks.getPage.mockResolvedValue(page('consignes'));

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'Consignes' }),
    ).toBeVisible();
    expect(
      screen.getByText(
        'Aucune racine de navigation n’est définie pour ce contexte.',
      ),
    ).toBeVisible();
    expect(screen.queryByText('Accueil du contexte')).not.toBeInTheDocument();
  });

  it('demande le PIN pour une page protégée et affiche les erreurs de saisie', async () => {
    setUrl('/confidentiel');
    mocks.getNavigation.mockResolvedValue({
      ...navigation,
      root: {
        ...root,
        title: 'Procédure confidentielle',
        slug: 'confidentiel',
        requiresAuthentication: true,
      },
      children: [],
    });
    mocks.getPage.mockRejectedValue(new PublicApiError('pinRequired'));
    mocks.verifyPin.mockRejectedValue(new PublicApiError('invalidPin'));

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'Procédure confidentielle' }),
    ).toBeVisible();
    fireEvent.change(screen.getByLabelText('PIN'), {
      target: { value: '123456' },
    });
    const form = screen.getByLabelText('PIN').closest('form');
    if (!form) throw new Error('PIN form was not rendered');
    fireEvent.submit(form);

    await waitFor(() =>
      expect(mocks.verifyPin).toHaveBeenCalledWith(invocation, '123456'),
    );
    expect(
      await screen.findByText('Ce PIN est incorrect. Réessaie.'),
    ).toBeVisible();
  });

  it('permet de revenir à la racine depuis une demande de PIN', async () => {
    setUrl('/confidentiel');
    const protectedItem = {
      nodeId: 'private-node',
      pageId: 'private-page',
      title: 'Procédure confidentielle',
      slug: 'confidentiel',
      isLink: false,
      requiresAuthentication: true,
      children: [],
    };
    mocks.getNavigation.mockResolvedValue({
      ...navigation,
      root: { ...root, children: [protectedItem] },
      children: [protectedItem],
    });
    mocks.getPage.mockImplementation(async (slug: string) => {
      if (slug === 'accueil') return page('accueil', 'Accueil du manuel.');
      throw new PublicApiError('pinRequired');
    });

    render(<App />);
    expect(
      await screen.findByRole('heading', { name: 'Procédure confidentielle' }),
    ).toBeVisible();
    fireEvent.click(
      screen.getByRole('link', { name: 'Retour à la racine du contexte' }),
    );

    expect(
      await screen.findByRole('heading', { name: 'Accueil' }),
    ).toBeVisible();
    expect(window.location.search).toBe(
      '?context=habitant&profile=desktop&device=ordinateur-bureau',
    );
  });

  it('conserve le contexte, le profil et le device lors de la navigation', async () => {
    setUrl('/');
    const child = {
      nodeId: 'child-node',
      pageId: 'child-page',
      title: 'Consignes',
      slug: 'consignes',
      isLink: false,
      requiresAuthentication: false,
      children: [],
    };
    mocks.getNavigation.mockResolvedValue({
      ...navigation,
      root: { ...root, children: [child] },
      children: [child],
    });
    mocks.getPage.mockImplementation(async (slug: string) => page(slug));

    render(<App />);
    expect(
      await screen.findByRole('heading', { name: 'Accueil' }),
    ).toBeVisible();

    fireEvent.click(screen.getByRole('link', { name: 'Consignes' }));

    expect(
      await screen.findByRole('heading', { name: 'Consignes' }),
    ).toBeVisible();
    expect(mocks.getPage).toHaveBeenLastCalledWith('consignes', invocation);
    expect(window.location.search).toBe(
      '?context=habitant&profile=desktop&device=ordinateur-bureau',
    );
  });

  it('n’interprète ni le HTML brut ni les liens externes du Markdown', async () => {
    setUrl('/consignes');
    mocks.getNavigation.mockResolvedValue(navigation);
    mocks.getPage.mockResolvedValue(
      page(
        'consignes',
        '<script>alert(1)</script>\n\n[Lien externe](https://example.com)\n\n[Page locale](/cuisine)',
      ),
    );

    const { container } = render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'Consignes' }),
    ).toBeVisible();
    expect(container.querySelector('script')).toBeNull();
    expect(screen.getByText('Lien externe').closest('a')).toBeNull();
    expect(screen.getByRole('link', { name: 'Page locale' })).toHaveAttribute(
      'href',
      '/cuisine?context=habitant&profile=desktop&device=ordinateur-bureau',
    );
  });

  it('verrouille le contenu protégé après trente secondes sans activité', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] });
    setUrl('/confidentiel');
    mocks.getNavigation.mockResolvedValue({
      ...navigation,
      root: {
        ...root,
        title: 'Confidentiel',
        slug: 'confidentiel',
        requiresAuthentication: true,
      },
    });
    mocks.getPage.mockResolvedValue({
      ...page('confidentiel', 'Texte protégé.'),
      requiresAuthentication: true,
    });

    render(<App />);
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(screen.getByText('Texte protégé.')).toBeVisible();

    await act(async () => {
      vi.advanceTimersByTime(29_999);
    });
    expect(screen.getByText('Texte protégé.')).toBeVisible();
    mocks.refreshProtectedAccess.mockResolvedValue(true);
    await act(async () => {
      fireEvent.pointerDown(window);
      await Promise.resolve();
    });
    expect(mocks.refreshProtectedAccess).toHaveBeenCalledWith(invocation);
    await act(async () => {
      vi.advanceTimersByTime(29_999);
    });
    expect(screen.getByText('Texte protégé.')).toBeVisible();
    await act(async () => {
      vi.advanceTimersByTime(1);
    });

    expect(screen.queryByText('Texte protégé.')).toBeNull();
    expect(screen.getByLabelText('PIN')).toBeVisible();
  });

  it('recherche au fil de la saisie, signale les pages protégées et ouvre un résultat', async () => {
    setUrl('/');
    mocks.getNavigation.mockResolvedValue(navigation);
    mocks.getPage.mockImplementation(async (slug: string) => page(slug));
    mocks.searchPages.mockResolvedValue({
      query: 'sopalin',
      corrected: false,
      results: [
        {
          pageId: 'essuie-id',
          title: 'Essuie-tout',
          slug: 'essuie-tout',
          category: 'emplacement',
          summary: 'Résumé.',
          excerpt: 'Sous l’évier.',
          requiresAuthentication: false,
          path: ['Cuisine'],
        },
        {
          pageId: 'coffre-id',
          title: 'Coffre',
          slug: 'coffre',
          category: 'equipement',
          summary: 'Résumé du coffre.',
          excerpt: null,
          requiresAuthentication: true,
          path: [],
        },
      ],
    });

    render(<App />);
    expect(
      await screen.findByRole('heading', { name: 'Accueil' }),
    ).toBeVisible();
    const input = screen.getByRole('searchbox', {
      name: 'Rechercher dans le manuel',
    });

    fireEvent.change(input, { target: { value: 's' } });
    await new Promise((resolve) => setTimeout(resolve, 300));
    expect(mocks.searchPages).not.toHaveBeenCalled();

    fireEvent.change(input, { target: { value: 'sopalin' } });
    const link = await screen.findByRole('link', { name: /Essuie-tout/ });
    expect(mocks.searchPages).toHaveBeenCalledTimes(1);
    expect(mocks.searchPages.mock.calls[0]?.[0]).toBe('sopalin');
    expect(mocks.searchPages.mock.calls[0]?.[1]).toEqual(invocation);
    expect(screen.getByText(/Emplacement · Cuisine/)).toBeVisible();
    expect(screen.getByText('Sous l’évier.')).toBeVisible();
    expect(screen.getByText('Résumé du coffre.')).toBeVisible();
    expect(
      screen.getByRole('link', { name: /Coffre.*Protégée/ }),
    ).toBeVisible();

    fireEvent.click(link);
    expect(
      await screen.findByRole('heading', { name: 'Consignes' }),
    ).toBeVisible();
    expect(mocks.getPage).toHaveBeenLastCalledWith('essuie-tout', invocation);
    expect(input).toHaveValue('');
  });

  it('indique les résultats approchants, l’absence de résultat et les pannes', async () => {
    setUrl('/');
    mocks.getNavigation.mockResolvedValue(navigation);
    mocks.getPage.mockImplementation(async (slug: string) => page(slug));
    mocks.searchPages.mockResolvedValueOnce({
      query: 'rouleaus',
      corrected: true,
      results: [
        {
          pageId: 'essuie-id',
          title: 'Essuie-tout',
          slug: 'essuie-tout',
          category: 'emplacement',
          summary: '',
          excerpt: null,
          requiresAuthentication: false,
          path: [],
        },
      ],
    });
    mocks.searchPages.mockResolvedValueOnce({
      query: 'zzzz',
      corrected: false,
      results: [],
    });
    mocks.searchPages.mockRejectedValueOnce(new PublicApiError('unavailable'));

    render(<App />);
    await screen.findByRole('heading', { name: 'Accueil' });
    const input = screen.getByRole('searchbox');

    fireEvent.change(input, { target: { value: 'rouleaus' } });
    expect(await screen.findByText(/Résultats approchants/)).toBeVisible();

    fireEvent.change(input, { target: { value: 'zzzz' } });
    expect(
      await screen.findByText(/Aucun résultat pour « zzzz »/),
    ).toBeVisible();

    fireEvent.change(input, { target: { value: 'panne' } });
    expect(
      await screen.findByText('La recherche est temporairement indisponible.'),
    ).toBeVisible();

    fireEvent.keyDown(input, { key: 'Escape' });
    expect(input).toHaveValue('');
    expect(screen.queryByText(/temporairement indisponible\./)).toBeNull();
  });

  it('affiche les images et les PDF du manuel avec le contexte, le profil et le device', async () => {
    setUrl('/');
    mocks.getNavigation.mockResolvedValue(navigation);
    mocks.getPage.mockResolvedValue(
      page(
        'accueil',
        '![Plan de la cuisine](/api/public/media/11111111-1111-1111-1111-111111111111)\n\n[Notice du four](/api/public/media/22222222-2222-2222-2222-222222222222)\n\n![Externe](https://exemple.test/image.png)',
      ),
    );

    render(<App />);
    const image = await screen.findByRole('img', {
      name: 'Plan de la cuisine',
    });
    const query = '?context=habitant&profile=desktop&device=ordinateur-bureau';
    expect(image).toHaveAttribute(
      'src',
      `/api/public/media/11111111-1111-1111-1111-111111111111${query}`,
    );
    expect(
      screen.getByRole('link', { name: 'Notice du four' }),
    ).toHaveAttribute(
      'href',
      `/api/public/media/22222222-2222-2222-2222-222222222222${query}`,
    );
    expect(screen.queryByRole('img', { name: 'Externe' })).toBeNull();
  });

  it('applies the appearance of the profile and removes it on unmount', async () => {
    const theme = {
      colorScheme: 'dark' as const,
      colors: {
        background: '#101010',
        surface: '#181818',
        text: '#f0f0f0',
        mutedText: '#bbbbbb',
        accent: '#ffd400',
        accentText: '#000000',
        border: '#333333',
        focus: '#00ffff',
      },
      style: {
        fontFamily: 'system',
        radiusPx: 8,
        shadow: 'none',
        iconSizePx: 24,
      },
    };
    mocks.getAppearance.mockResolvedValue({
      profile: 'desktop',
      themeMode: 'dark',
      light: theme,
      dark: theme,
      layout: {
        fontSizePx: 20,
        typeScale: 1,
        spacingScale: 1,
        maxColumns: 3,
        cardSize: 'comfortable',
        interactionMode: 'pointer',
        minTargetPx: 44,
        contentMaxWidthPx: 1000,
        showTouchActions: false,
        showPresentation: true,
      },
      customCss: '.public-page { letter-spacing: 0.02em; }',
    });
    mocks.getNavigation.mockResolvedValue(navigation);
    mocks.getPage.mockResolvedValue(page('accueil'));
    setUrl('/accueil');
    const view = render(<App />);
    await waitFor(() =>
      expect(document.documentElement.style.getPropertyValue('--hd-bg')).toBe(
        '#101010',
      ),
    );
    expect(
      document.documentElement.style.getPropertyValue('--hd-base-size'),
    ).toBe('20px');
    expect(
      document.head.querySelector('style[data-device-css]')?.textContent,
    ).toContain('letter-spacing');
    view.unmount();
    expect(document.head.querySelector('style[data-device-css]')).toBeNull();
    expect(document.documentElement.style.getPropertyValue('--hd-bg')).toBe('');
  });

  describe('clavier et télécommande', () => {
    const child = (slug: string, title: string) => ({
      ...root,
      nodeId: `${slug}-node`,
      pageId: `${slug}-page`,
      title,
      slug,
    });
    const withChildren = {
      ...navigation,
      root: {
        ...root,
        children: [child('cuisine', 'Cuisine'), child('salon', 'Salon')],
      },
      children: [child('cuisine', 'Cuisine'), child('salon', 'Salon')],
    };

    beforeEach(() => {
      mocks.getNavigation.mockResolvedValue(withChildren);
      mocks.getPage.mockImplementation(async (slug: string) => ({
        ...page(slug),
        title:
          slug === 'cuisine'
            ? 'Cuisine'
            : slug === 'salon'
              ? 'Salon'
              : 'Accueil',
        markdown: '[Autre page](/salon)\n\nTexte.',
      }));
    });

    it('propose un lien d’évitement et un contenu principal ciblable', async () => {
      setUrl('/accueil');
      render(<App />);
      const skip = await screen.findByRole('link', {
        name: 'Aller au contenu',
      });
      expect(skip).toHaveAttribute('href', '#contenu');
      expect(screen.getByRole('main')).toHaveAttribute('id', 'contenu');
    });

    it('déplace le focus avec les flèches entre la navigation et la page', async () => {
      setUrl('/cuisine');
      render(<App />);
      const cuisine = await screen.findByRole('link', { name: 'Cuisine' });
      const salon = screen.getByRole('link', { name: 'Salon' });
      cuisine.focus();

      fireEvent.keyDown(cuisine, { key: 'ArrowDown' });
      expect(salon).toHaveFocus();
      fireEvent.keyDown(salon, { key: 'ArrowUp' });
      expect(cuisine).toHaveFocus();

      fireEvent.keyDown(cuisine, { key: 'ArrowRight' });
      expect(screen.getByRole('main').contains(document.activeElement)).toBe(
        true,
      );

      fireEvent.keyDown(document.activeElement!, { key: 'ArrowLeft' });
      expect(cuisine).toHaveFocus();
    });

    it('Échap revient à la page précédente du manuel puis à la racine, sans quitter le manuel', async () => {
      setUrl('/accueil');
      window.history.replaceState({ depth: 0 }, '', window.location.href);
      render(<App />);
      fireEvent.click(await screen.findByRole('link', { name: 'Cuisine' }));
      expect(
        await screen.findByRole('heading', { name: 'Cuisine' }),
      ).toBeVisible();
      expect(window.history.state).toEqual({ depth: 1 });

      const back = vi
        .spyOn(window.history, 'back')
        .mockImplementation(() => {});
      fireEvent.keyDown(screen.getByRole('link', { name: 'Cuisine' }), {
        key: 'Escape',
      });
      expect(back).toHaveBeenCalledTimes(1);
      back.mockRestore();

      // Without history of its own, Back goes to the context root.
      window.history.replaceState({ depth: 0 }, '', window.location.href);
      fireEvent.keyDown(screen.getByRole('link', { name: 'Cuisine' }), {
        key: 'BrowserBack',
      });
      await waitFor(() => expect(window.location.pathname).toBe('/'));
    });

    it('laisse la recherche effacer sa saisie avant de revenir en arrière', async () => {
      setUrl('/cuisine');
      window.history.replaceState({ depth: 3 }, '', window.location.href);
      const back = vi
        .spyOn(window.history, 'back')
        .mockImplementation(() => {});
      mocks.searchPages.mockResolvedValue({ results: [], corrected: false });
      render(<App />);
      const input = await screen.findByRole('searchbox');
      fireEvent.change(input, { target: { value: 'four' } });
      fireEvent.keyDown(input, { key: 'Escape' });
      expect(back).not.toHaveBeenCalled();
      expect(input).toHaveValue('');
      fireEvent.keyDown(input, { key: 'Escape' });
      expect(back).toHaveBeenCalledTimes(1);
      back.mockRestore();
    });

    it('revient à la racine du contexte après inactivité sur un écran à télécommande', async () => {
      const theme = {
        colorScheme: 'light' as const,
        colors: {
          background: '#ffffff',
          surface: '#ffffff',
          text: '#000000',
          mutedText: '#333333',
          accent: '#006600',
          accentText: '#ffffff',
          border: '#cccccc',
          focus: '#cc6600',
        },
        style: {
          fontFamily: 'system',
          radiusPx: 8,
          shadow: 'none',
          iconSizePx: 24,
        },
      };
      mocks.getAppearance.mockResolvedValue({
        profile: 'desktop',
        themeMode: 'light',
        light: theme,
        dark: theme,
        layout: {
          fontSizePx: 24,
          typeScale: 1,
          spacingScale: 1,
          maxColumns: 3,
          cardSize: 'large',
          interactionMode: 'remote',
          minTargetPx: 64,
          contentMaxWidthPx: 1200,
          showTouchActions: false,
          showPresentation: true,
        },
      });
      setUrl('/cuisine');
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        render(<App />);
        await screen.findByRole('heading', { name: 'Cuisine' });
        await act(async () => {
          await vi.advanceTimersByTimeAsync(119_000);
        });
        expect(window.location.pathname).toBe('/cuisine');
        fireEvent.keyDown(document.body, { key: 'ArrowDown' }); // activity postpones the return
        await act(async () => {
          await vi.advanceTimersByTimeAsync(119_000);
        });
        expect(window.location.pathname).toBe('/cuisine');
        await act(async () => {
          await vi.advanceTimersByTimeAsync(2_000);
        });
        expect(window.location.pathname).toBe('/');
        expect(window.location.search).toContain('context=habitant');
      } finally {
        vi.useRealTimers();
      }
    });

    it('ne revient pas à la racine tout seul avec la souris ou le tactile', async () => {
      setUrl('/cuisine');
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        render(<App />);
        await screen.findByRole('heading', { name: 'Cuisine' });
        await act(async () => {
          await vi.advanceTimersByTimeAsync(600_000);
        });
        expect(window.location.pathname).toBe('/cuisine');
      } finally {
        vi.useRealTimers();
      }
    });

    it('met le focus sur la page après une navigation et l’annonce', async () => {
      setUrl('/accueil');
      render(<App />);
      fireEvent.click(await screen.findByRole('link', { name: 'Salon' }));
      await screen.findByRole('heading', { name: 'Salon' });
      await waitFor(() => expect(screen.getByRole('main')).toHaveFocus());
      expect(
        screen
          .getAllByRole('status')
          .some((node) => node.textContent === 'Salon'),
      ).toBe(true);
    });
  });
});
