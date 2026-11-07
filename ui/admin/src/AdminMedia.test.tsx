import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminMedia, MediaPicker } from './AdminMedia';
import { MediaError } from './adminMediaApi';
import type { MediaDetail, MediaItem } from './adminMediaApi';
import { translate } from './translations';
import type { TranslationKey } from './translations';

const mocks = vi.hoisted(() => ({
  listMedia: vi.fn(),
  getMedia: vi.fn(),
  uploadMedia: vi.fn(),
  updateMedia: vi.fn(),
  deleteMedia: vi.fn(),
}));

vi.mock('./adminMediaApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./adminMediaApi')>();
  return { ...actual, ...mocks };
});

const t = (key: TranslationKey) => translate('fr', key);

const plan: MediaItem = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'plan.png',
  kind: 'image',
  contentType: 'image/png',
  size: 2048,
  title: 'Plan de la cuisine',
  altText: 'Vue du dessus',
  folder: 'Cuisine',
  createdAt: '2026-11-07T20:00:00Z',
  revision: 1,
  url: '/api/public/media/11111111-1111-1111-1111-111111111111',
  markdown:
    '![Vue du dessus](/api/public/media/11111111-1111-1111-1111-111111111111)',
  usageCount: 1,
};

const usedDetail: MediaDetail = {
  media: plan,
  usages: [
    {
      pageId: '22222222-2222-2222-2222-222222222222',
      title: 'Cuisine',
      slug: 'cuisine',
      isPublished: true,
    },
  ],
};

describe('médias', () => {
  beforeEach(() => {
    Object.values(mocks).forEach((mock) => mock.mockReset());
    mocks.listMedia.mockResolvedValue([plan]);
    mocks.getMedia.mockResolvedValue(usedDetail);
  });

  afterEach(() => cleanup());

  it('liste les médias, montre leurs utilisations et protège la suppression', async () => {
    const onNavigate = vi.fn();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    render(
      <AdminMedia
        translate={t}
        onNavigate={onNavigate}
        onSessionExpired={vi.fn()}
      />,
    );

    fireEvent.click(
      await screen.findByRole('button', { name: /Plan de la cuisine/ }),
    );
    expect(await screen.findByLabelText('Nom du fichier')).toHaveValue(
      'plan.png',
    );
    expect(screen.getByLabelText('Markdown à insérer')).toHaveValue(
      plan.markdown,
    );
    fireEvent.click(screen.getByRole('link', { name: 'Cuisine' }));
    expect(onNavigate).toHaveBeenCalledWith({
      type: 'content-edit',
      id: '22222222-2222-2222-2222-222222222222',
    });

    mocks.deleteMedia.mockRejectedValueOnce(
      new MediaError('inUse', usedDetail.usages),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer le média' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ce média est utilisé par les pages ci-dessous',
    );
    expect(mocks.deleteMedia).toHaveBeenLastCalledWith(plan, false);

    mocks.deleteMedia.mockResolvedValueOnce(undefined);
    mocks.listMedia.mockResolvedValue([]);
    fireEvent.click(
      screen.getByRole('button', { name: 'Supprimer quand même' }),
    );
    await waitFor(() =>
      expect(mocks.deleteMedia).toHaveBeenLastCalledWith(plan, true),
    );
    expect(await screen.findByText('Média supprimé.')).toBeVisible();
    confirm.mockRestore();
  });

  it('enregistre le titre, le texte alternatif et le dossier', async () => {
    mocks.updateMedia.mockResolvedValue({
      media: { ...plan, altText: 'Plan', revision: 2 },
      usages: [],
    });
    render(
      <AdminMedia
        translate={t}
        onNavigate={vi.fn()}
        onSessionExpired={vi.fn()}
      />,
    );
    fireEvent.click(
      await screen.findByRole('button', { name: /Plan de la cuisine/ }),
    );
    fireEvent.change(await screen.findByLabelText('Texte alternatif'), {
      target: { value: 'Plan' },
    });
    fireEvent.click(
      screen.getByRole('button', { name: 'Enregistrer le média' }),
    );

    await waitFor(() =>
      expect(mocks.updateMedia).toHaveBeenCalledWith(plan, {
        name: 'plan.png',
        title: 'Plan de la cuisine',
        altText: 'Plan',
        folder: 'Cuisine',
      }),
    );
    expect(await screen.findByText('Média enregistré.')).toBeVisible();
  });

  it('importe un fichier, ou signale un format refusé', async () => {
    mocks.uploadMedia.mockRejectedValueOnce(new MediaError('unsupported'));
    render(
      <AdminMedia
        translate={t}
        onNavigate={vi.fn()}
        onSessionExpired={vi.fn()}
      />,
    );
    const input = await screen.findByLabelText('Importer des médias');
    const file = new File(['x'], 'logo.svg', { type: 'image/svg+xml' });
    fireEvent.change(input, { target: { files: [file] } });
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Format non pris en charge',
    );
  });

  it('insère le Markdown du média choisi sans saisir de chemin', async () => {
    const onInsert = vi.fn();
    render(
      <MediaPicker t={t} onInsert={onInsert} onSessionExpired={vi.fn()} />,
    );
    fireEvent.click(await screen.findByRole('button', { name: 'Insérer' }));
    expect(onInsert).toHaveBeenCalledWith(plan);
  });

  it('insère un média juste après son import', async () => {
    const onInsert = vi.fn();
    mocks.uploadMedia.mockResolvedValue({ media: plan, usages: [] });
    render(
      <MediaPicker t={t} onInsert={onInsert} onSessionExpired={vi.fn()} />,
    );
    const input = await screen.findByLabelText('Importer des médias');
    fireEvent.change(input, {
      target: { files: [new File(['x'], 'plan.png', { type: 'image/png' })] },
    });
    await waitFor(() => expect(onInsert).toHaveBeenCalledWith(plan));
  });
});
