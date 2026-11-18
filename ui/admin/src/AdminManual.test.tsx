import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminManual } from './AdminManual';
import { translate } from './translations';
import type { TranslationKey } from './translations';

const mocks = vi.hoisted(() => ({
  listContexts: vi.fn(),
  updateContext: vi.fn(),
  listTree: vi.fn(),
  createLink: vi.fn(),
  removeNode: vi.fn(),
  listPages: vi.fn(),
  getPage: vi.fn(),
  createPage: vi.fn(),
}));

vi.mock('./adminContextApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./adminContextApi')>();
  return {
    ...actual,
    listContexts: mocks.listContexts,
    updateContext: mocks.updateContext,
  };
});
vi.mock('./adminTreeApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./adminTreeApi')>();
  return {
    ...actual,
    listTree: mocks.listTree,
    createLink: mocks.createLink,
    removeNode: mocks.removeNode,
  };
});
vi.mock('./adminContentApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./adminContentApi')>();
  return {
    ...actual,
    listPages: mocks.listPages,
    getPage: mocks.getPage,
    createPage: mocks.createPage,
  };
});
// The Markdown editor needs a real browser; a textarea is enough here.
vi.mock('@uiw/react-codemirror', () => ({
  default: ({
    value,
    onChange,
  }: {
    value: string;
    onChange: (value: string) => void;
  }) => (
    <textarea
      aria-label="markdown"
      value={value}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
}));

const t = (key: TranslationKey) => translate('fr', key);

const page = (id: string, title: string, slug: string, isPublished = true) => ({
  id,
  title,
  slug,
  category: 'page',
  contexts: [],
  requiresAuthentication: false,
  isPublished,
  markdown: '# ' + title,
  revision: 1,
});
const node = (
  id: string,
  parentNodeId: string | null,
  pageId: string,
  title: string,
  slug: string,
  isLink = false,
  position = 0,
) => ({
  id,
  parentNodeId,
  pageId,
  title,
  slug,
  isLink,
  position,
});

function renderPage() {
  const onNavigate = vi.fn();
  render(
    <AdminManual
      translate={t}
      onNavigate={onNavigate}
      onSessionExpired={vi.fn()}
    />,
  );
  return { onNavigate };
}

describe('manuel', () => {
  beforeEach(() => {
    Object.values(mocks).forEach((mock) => mock.mockReset());
    window.localStorage.clear();
    mocks.listPages.mockResolvedValue([
      page('p-home', 'Accueil', 'accueil'),
      page('p-cuisine', 'Cuisine', 'cuisine'),
      page('p-four', 'Four', 'four', false),
    ]);
    mocks.getPage.mockImplementation(async (id: string) =>
      id === 'p-four'
        ? page('p-four', 'Four', 'four', false)
        : page('p-home', 'Accueil', 'accueil'),
    );
  });
  afterEach(cleanup);

  it('affiche l’arborescence du contexte choisi dans la barre latérale', async () => {
    mocks.listContexts.mockResolvedValue([
      { id: 'maison', name: 'Maison', rootNodeId: 'n-home' },
      { id: 'invites', name: 'Invités', rootNodeId: null },
    ]);
    mocks.listTree.mockResolvedValue([
      node('n-home', null, 'p-home', 'Accueil', 'accueil'),
      node('n-cuisine', 'n-home', 'p-cuisine', 'Cuisine', 'cuisine'),
      node('n-four', 'n-cuisine', 'p-four', 'Four', 'four'),
    ]);
    renderPage();

    const tree = await screen.findByRole('complementary', {
      name: t('manualTreeTitle'),
    });
    expect(
      within(tree).getByRole('button', { name: /^Cuisine/ }),
    ).toBeVisible();
    expect(
      within(tree).getByRole('button', { name: /^Four/ }),
    ).toHaveTextContent(t('manualDraftTag'));

    expect(within(tree).queryByRole('note')).not.toBeInTheDocument();

    // A context without root offers to create it.
    fireEvent.change(screen.getByLabelText(t('manualContext')), {
      target: { value: 'invites' },
    });
    expect(await screen.findByText(t('manualNoRoot'))).toBeVisible();
    expect(
      screen.getByRole('button', { name: t('manualCreateRoot') }),
    ).toBeVisible();
  });

  it('crée la page racine d’un contexte sans racine et la lui attribue', async () => {
    mocks.listContexts.mockResolvedValue([
      { id: 'invites', name: 'Invités', rootNodeId: null },
    ]);
    mocks.listTree
      .mockResolvedValueOnce([])
      .mockResolvedValue([
        node('n-new', null, 'p-new', 'Bienvenue', 'bienvenue'),
      ]);
    mocks.createPage.mockResolvedValue('p-new');
    mocks.getPage.mockResolvedValue(page('p-new', 'Bienvenue', 'bienvenue'));
    mocks.updateContext.mockResolvedValue(undefined);
    renderPage();

    fireEvent.click(
      await screen.findByRole('button', { name: t('manualCreateRoot') }),
    );
    fireEvent.change(await screen.findByLabelText(/Titre|Title/), {
      target: { value: 'Bienvenue à la maison' },
    });
    // The address is proposed from the title.
    expect(screen.getByLabelText(/Slug|Adresse/)).toHaveValue(
      'bienvenue-a-la-maison',
    );
    fireEvent.click(screen.getByRole('button', { name: t('savePage') }));

    await waitFor(() => expect(mocks.createPage).toHaveBeenCalled());
    const [input, parent] = mocks.createPage.mock.calls[0];
    expect(parent).toBeNull();
    expect(input.slug).toBe('bienvenue-a-la-maison');
    await waitFor(() =>
      expect(mocks.updateContext).toHaveBeenCalledWith('invites', {
        id: 'invites',
        name: 'Invités',
        rootNodeId: 'n-new',
      }),
    );
  });

  it('crée une sous-page sous le nœud choisi, sans demander l’emplacement', async () => {
    mocks.listContexts.mockResolvedValue([
      { id: 'maison', name: 'Maison', rootNodeId: 'n-home' },
    ]);
    mocks.listTree.mockResolvedValue([
      node('n-home', null, 'p-home', 'Accueil', 'accueil'),
    ]);
    mocks.createPage.mockResolvedValue('p-sub');
    mocks.getPage.mockResolvedValue(page('p-sub', 'Chauffage', 'chauffage'));
    renderPage();

    fireEvent.click(
      await screen.findByRole('button', {
        name: `${t('manualAddSubpageTo')} Accueil`,
      }),
    );
    expect(screen.queryByLabelText(t('placement'))).not.toBeInTheDocument();
    fireEvent.change(await screen.findByLabelText(/Titre|Title/), {
      target: { value: 'Chauffage' },
    });
    fireEvent.click(screen.getByRole('button', { name: t('savePage') }));

    await waitFor(() => expect(mocks.createPage).toHaveBeenCalled());
    expect(mocks.createPage.mock.calls[0][1]).toBe('n-home');
    expect(mocks.updateContext).not.toHaveBeenCalled();
  });

  it('lie une page existante trouvée par la recherche', async () => {
    mocks.listContexts.mockResolvedValue([
      { id: 'maison', name: 'Maison', rootNodeId: 'n-home' },
    ]);
    mocks.listTree.mockResolvedValue([
      node('n-home', null, 'p-home', 'Accueil', 'accueil'),
    ]);
    mocks.createLink.mockResolvedValue(undefined);
    renderPage();

    fireEvent.click(
      await screen.findByRole('button', {
        name: `${t('manualAddLinkTo')} Accueil`,
      }),
    );
    fireEvent.change(await screen.findByLabelText(t('manualLinkSearch')), {
      target: { value: 'cuis' },
    });
    expect(screen.queryByText('Four')).not.toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: t('manualLinkAction') }),
    );

    await waitFor(() =>
      expect(mocks.createLink).toHaveBeenCalledWith('p-cuisine', 'n-home'),
    );
    expect(await screen.findByText(t('manualLinkCreated'))).toBeVisible();
  });

  it('prévient que le manuel public reste vide tant que la racine est un brouillon', async () => {
    mocks.listContexts.mockResolvedValue([
      { id: 'maison', name: 'Maison', rootNodeId: 'n-four' },
    ]);
    mocks.listTree.mockResolvedValue([
      node('n-four', null, 'p-four', 'Four', 'four'),
    ]);
    renderPage();
    expect(await screen.findByRole('note')).toHaveTextContent(
      t('manualRootDraft'),
    );
  });
});
