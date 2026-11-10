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
    view.unmount();
    expect(document.documentElement.style.getPropertyValue('--hd-bg')).toBe('');
  });
});
