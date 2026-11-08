import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PageHistoryPanel, PageLinksPanel } from './AdminPageHistory';
import { HistoryError } from './adminHistoryApi';
import type { VersionSummary } from './adminHistoryApi';
import { translate } from './translations';
import type { TranslationKey } from './translations';

const mocks = vi.hoisted(() => ({
  listVersions: vi.fn(),
  getVersion: vi.fn(),
  restoreVersion: vi.fn(),
  getLinks: vi.fn(),
}));

vi.mock('./adminHistoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./adminHistoryApi')>();
  return { ...actual, ...mocks };
});

const t = (key: TranslationKey) => translate('fr', key);

function version(revision: number, event: string): VersionSummary {
  return {
    revision,
    event,
    title: `Titre ${revision}`,
    slug: 'four',
    isPublished: false,
    requiresAuthentication: false,
    savedAt: '2026-11-07T20:00:00Z',
    size: 10,
    restoredFromRevision: null,
  };
}

describe('historique et liens', () => {
  beforeEach(() => {
    Object.values(mocks).forEach((mock) => mock.mockReset());
  });
  afterEach(() => cleanup());

  it('liste les versions, affiche le contenu et restaure une version antérieure', async () => {
    mocks.listVersions.mockResolvedValue([
      version(3, 'edited'),
      version(2, 'published'),
      version(1, 'created'),
    ]);
    mocks.getVersion.mockResolvedValue({
      summary: version(1, 'created'),
      category: 'page',
      contexts: [],
      markdown: 'Contenu de la version un.',
    });
    mocks.restoreVersion.mockResolvedValue(undefined);
    const onRestored = vi.fn();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);

    render(
      <PageHistoryPanel
        t={t}
        pageId="page-id"
        revision={3}
        onRestored={onRestored}
        onSessionExpired={vi.fn()}
      />,
    );

    expect(await screen.findByText(/Version 3/)).toBeVisible();
    // The newest version is the current one: it cannot be restored.
    expect(
      screen.getAllByRole('button', { name: 'Restaurer cette version' }),
    ).toHaveLength(2);

    fireEvent.click(screen.getAllByRole('button', { name: 'Afficher' })[2]!);
    expect(await screen.findByText('Contenu de la version un.')).toBeVisible();

    fireEvent.click(
      screen.getAllByRole('button', { name: 'Restaurer cette version' })[1]!,
    );
    await waitFor(() =>
      expect(mocks.restoreVersion).toHaveBeenCalledWith('page-id', 3, 1),
    );
    expect(onRestored).toHaveBeenCalled();
    expect(await screen.findByText('Version restaurée.')).toBeVisible();
    confirm.mockRestore();
  });

  it('signale un conflit de révision lors de la restauration', async () => {
    mocks.listVersions.mockResolvedValue([
      version(2, 'edited'),
      version(1, 'created'),
    ]);
    mocks.restoreVersion.mockRejectedValue(new HistoryError('conflict'));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    render(
      <PageHistoryPanel
        t={t}
        pageId="page-id"
        revision={2}
        onRestored={vi.fn()}
        onSessionExpired={vi.fn()}
      />,
    );
    fireEvent.click(
      await screen.findByRole('button', { name: 'Restaurer cette version' }),
    );
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'La page a changé',
    );
    confirm.mockRestore();
  });

  it('affiche les liens sortants avec leur état et les liens entrants', async () => {
    mocks.getLinks.mockResolvedValue({
      outgoing: [
        {
          href: '/cuisine',
          kind: 'page',
          status: 'ok',
          targetId: 'cuisine-id',
          targetTitle: 'Cuisine',
        },
        {
          href: '/disparue',
          kind: 'page',
          status: 'broken',
          targetId: null,
          targetTitle: null,
        },
        {
          href: 'https://exemple.test',
          kind: 'external',
          status: 'unchecked',
          targetId: null,
          targetTitle: null,
        },
      ],
      incoming: [
        {
          pageId: 'accueil-id',
          title: 'Accueil',
          slug: 'accueil',
          isPublished: true,
        },
      ],
    });
    const onNavigatePage = vi.fn();
    render(
      <PageLinksPanel
        t={t}
        pageId="page-id"
        revision={1}
        onNavigatePage={onNavigatePage}
        onSessionExpired={vi.fn()}
      />,
    );

    expect(await screen.findByText('/disparue')).toBeVisible();
    expect(screen.getByText('cassé')).toBeVisible();
    expect(screen.getByText('externe, non vérifié')).toBeVisible();
    fireEvent.click(screen.getByRole('link', { name: 'Accueil' }));
    expect(onNavigatePage).toHaveBeenCalledWith('accueil-id');
  });
});
