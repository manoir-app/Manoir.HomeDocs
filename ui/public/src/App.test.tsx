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
    mocks.getNavigation.mockReset();
    mocks.getPage.mockReset();
    mocks.verifyPin.mockReset();
    mocks.refreshProtectedAccess.mockReset();
    mocks.logoutProtectedAccess.mockReset();
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
});
