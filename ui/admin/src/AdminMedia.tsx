import { useCallback, useEffect, useRef, useState } from 'react';
import type { ChangeEvent, FormEvent } from 'react';
import { Button, Card, Field, TextField } from '@manoir-app/core-admin-ui-kit';
import * as api from './adminMediaApi';
import type {
  MediaDetail,
  MediaFilter,
  MediaItem,
  MediaUsage,
} from './adminMediaApi';
import type { AdminRoute } from './adminRoutes';
import type { TranslationKey } from './translations';

type Translate = (key: TranslationKey) => string;

const acceptedFiles =
  'image/png,image/jpeg,image/gif,image/webp,application/pdf';

const emptyFilter: MediaFilter = { q: '', folder: '', kind: '' };

function mediaFailureKey(error: api.MediaFailure): TranslationKey {
  if (error === 'invalid') return 'mediaInvalid';
  if (error === 'unsupported') return 'mediaUnsupported';
  if (error === 'tooLarge') return 'mediaTooLarge';
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'notFound') return 'mediaNotFound';
  if (error === 'conflict' || error === 'inUse') return 'mediaConflict';
  if (error === 'throttled') return 'mediaRateLimit';
  return 'mediaUnavailable';
}

function failureOf(reason: unknown): api.MediaFailure {
  return reason instanceof api.MediaError ? reason.reason : 'unavailable';
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} o`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} Ko`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} Mo`;
}

/** Loads the library for a filter and keeps the latest answer only. */
function useMediaList(
  filter: MediaFilter,
  onSessionExpired: () => void,
): {
  items: MediaItem[];
  loading: boolean;
  error: api.MediaFailure | undefined;
  reload: () => Promise<void>;
} {
  const [items, setItems] = useState<MediaItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<api.MediaFailure>();
  const revision = useRef(0);
  const { q, folder, kind } = filter;

  const reload = useCallback(async () => {
    const requestId = ++revision.current;
    setLoading(true);
    try {
      const result = await api.listMedia({ q, folder, kind });
      if (requestId !== revision.current) return;
      setItems(result);
      setError(undefined);
    } catch (reason) {
      if (requestId !== revision.current) return;
      const failure = failureOf(reason);
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      if (requestId === revision.current) setLoading(false);
    }
  }, [q, folder, kind, onSessionExpired]);

  useEffect(() => {
    const activeRevision = revision;
    let active = true;
    queueMicrotask(() => {
      if (active) void reload();
    });
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [reload]);

  return { items, loading, error, reload };
}

function MediaSearch({
  t,
  filter,
  folders,
  onChange,
}: {
  t: Translate;
  filter: MediaFilter;
  folders: string[];
  onChange: (filter: MediaFilter) => void;
}) {
  const [text, setText] = useState(filter.q);
  const submit = () => onChange({ ...filter, q: text });
  // Not a <form>: the picker lives inside the page editor form, and forms cannot be nested.
  return (
    <div className="media-search" role="search">
      <label className="content-field" htmlFor="media-search">
        <span>{t('mediaSearch')}</span>
        <input
          id="media-search"
          type="search"
          maxLength={100}
          value={text}
          onChange={(event) => setText(event.target.value)}
          onKeyDown={(event) => {
            if (event.key !== 'Enter') return;
            event.preventDefault();
            submit();
          }}
        />
      </label>
      <label className="content-field" htmlFor="media-folder-filter">
        <span>{t('mediaFolder')}</span>
        <select
          id="media-folder-filter"
          value={filter.folder}
          onChange={(event) =>
            onChange({ ...filter, q: text, folder: event.target.value })
          }
        >
          <option value="">{t('mediaAllFolders')}</option>
          {folders.map((folder) => (
            <option key={folder} value={folder}>
              {folder}
            </option>
          ))}
        </select>
      </label>
      <label className="content-field" htmlFor="media-kind-filter">
        <span>
          {t('mediaKindImage')} / {t('mediaKindPdf')}
        </span>
        <select
          id="media-kind-filter"
          value={filter.kind}
          onChange={(event) =>
            onChange({
              ...filter,
              q: text,
              kind: event.target.value as MediaFilter['kind'],
            })
          }
        >
          <option value="">{t('mediaAllKinds')}</option>
          <option value="image">{t('mediaKindImage')}</option>
          <option value="pdf">{t('mediaKindPdf')}</option>
        </select>
      </label>
      <Button type="button" variant="secondary" onClick={submit}>
        {t('mediaSearchSubmit')}
      </Button>
    </div>
  );
}

function Thumbnail({ media }: { media: MediaItem }) {
  return media.kind === 'image' ? (
    <img
      className="media-thumbnail"
      src={media.url}
      alt={media.altText}
      loading="lazy"
    />
  ) : (
    <span className="media-thumbnail media-thumbnail-pdf" aria-hidden="true">
      PDF
    </span>
  );
}

function UploadButton({
  t,
  folder,
  busy,
  onUploaded,
  onError,
  onBusy,
}: {
  t: Translate;
  folder: string;
  busy: boolean;
  onUploaded: (detail: MediaDetail) => void;
  onError: (failure: api.MediaFailure) => void;
  onBusy: (busy: boolean) => void;
}) {
  const choose = async (event: ChangeEvent<HTMLInputElement>) => {
    const input = event.currentTarget;
    const files = [...(input.files ?? [])];
    input.value = '';
    if (files.length === 0) return;
    onBusy(true);
    try {
      for (const file of files) {
        onUploaded(
          await api.uploadMedia(file, { title: '', altText: '', folder }),
        );
      }
    } catch (reason) {
      onError(failureOf(reason));
    } finally {
      onBusy(false);
    }
  };
  return (
    <div className="media-upload">
      <label className="content-field" htmlFor="media-upload">
        <span>{busy ? t('mediaUploading') : t('mediaUpload')}</span>
        <input
          id="media-upload"
          type="file"
          multiple
          accept={acceptedFiles}
          disabled={busy}
          onChange={(event) => void choose(event)}
        />
      </label>
      <p className="content-help">{t('mediaUploadHelp')}</p>
    </div>
  );
}

export function AdminMedia({
  translate: t,
  onNavigate,
  onSessionExpired,
}: {
  translate: Translate;
  onNavigate: (route: AdminRoute) => void;
  onSessionExpired: () => void;
}) {
  const [filter, setFilter] = useState<MediaFilter>(emptyFilter);
  const {
    items,
    loading,
    error: listError,
    reload,
  } = useMediaList(filter, onSessionExpired);
  const [folders, setFolders] = useState<string[]>([]);
  const [detail, setDetail] = useState<MediaDetail>();
  const [form, setForm] = useState({
    name: '',
    title: '',
    altText: '',
    folder: '',
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<api.MediaFailure>();
  const [notice, setNotice] = useState<TranslationKey>();
  const [blockingUsages, setBlockingUsages] = useState<MediaUsage[]>();

  useEffect(() => {
    // Folders are collected from the unfiltered library so the filter never hides its own options.
    if (!filter.q && !filter.folder && !filter.kind) {
      queueMicrotask(() =>
        setFolders(
          [...new Set(items.map((item) => item.folder).filter(Boolean))].sort(
            (a, b) => a.localeCompare(b),
          ),
        ),
      );
    }
  }, [items, filter]);

  const failWith = (failure: api.MediaFailure) => {
    setError(failure);
    if (failure === 'sessionExpired') onSessionExpired();
  };
  const fail = (reason: unknown) => failWith(failureOf(reason));

  const show = (next: MediaDetail | undefined) => {
    setDetail(next);
    setBlockingUsages(undefined);
    if (next) {
      setForm({
        name: next.media.name,
        title: next.media.title,
        altText: next.media.altText,
        folder: next.media.folder,
      });
    }
  };

  const select = async (media: MediaItem) => {
    setError(undefined);
    setNotice(undefined);
    try {
      show(await api.getMedia(media.id));
    } catch (reason) {
      fail(reason);
    }
  };

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!detail) return;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      show(await api.updateMedia(detail.media, form));
      setNotice('mediaSaved');
      await reload();
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const remove = async (force: boolean) => {
    if (!detail) return;
    if (!force && !window.confirm(t('mediaDeleteConfirm'))) return;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      await api.deleteMedia(detail.media, force);
      show(undefined);
      setNotice('mediaDeleted');
      await reload();
    } catch (reason) {
      if (reason instanceof api.MediaError && reason.reason === 'inUse') {
        setBlockingUsages(reason.usages);
      } else {
        fail(reason);
      }
    } finally {
      setBusy(false);
    }
  };

  const shownError = error ?? listError;

  return (
    <main className="mn-kit-theme content-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={() => onNavigate(null)}>
          {t('backToAdmin')}
        </Button>
      </header>
      <section className="media-content">
        <div className="content-heading">
          <div>
            <h1>{t('mediaLibrary')}</h1>
            <p>{t('mediaDescription')}</p>
          </div>
        </div>
        {shownError && <p role="alert">{t(mediaFailureKey(shownError))}</p>}
        {notice && <p role="status">{t(notice)}</p>}
        <div className="media-layout">
          <Card className="media-list">
            <MediaSearch
              t={t}
              filter={filter}
              folders={folders}
              onChange={setFilter}
            />
            <UploadButton
              t={t}
              folder={filter.folder}
              busy={busy}
              onBusy={setBusy}
              onError={failWith}
              onUploaded={(uploaded) => {
                setError(undefined);
                setNotice('mediaUploaded');
                show(uploaded);
                void reload();
              }}
            />
            {loading ? (
              <p role="status">{t('loading')}</p>
            ) : items.length === 0 ? (
              <p>{t('mediaNone')}</p>
            ) : (
              <ul className="media-items">
                {items.map((media) => (
                  <li key={media.id}>
                    <button
                      type="button"
                      aria-current={
                        detail?.media.id === media.id ? 'true' : undefined
                      }
                      onClick={() => void select(media)}
                    >
                      <Thumbnail media={media} />
                      <span>
                        <strong>{media.title || media.name}</strong>
                        <small>
                          {media.name}
                          {media.folder ? ` · ${media.folder}` : ''} ·{' '}
                          {formatSize(media.size)} · {media.usageCount}{' '}
                          {t('mediaUsageCount')}
                        </small>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>
          <Card className="media-form-card">
            {!detail ? (
              <p>{t('mediaSelect')}</p>
            ) : (
              <>
                <h2>{detail.media.title || detail.media.name}</h2>
                <div className="media-preview">
                  {detail.media.kind === 'image' ? (
                    <img src={detail.media.url} alt={detail.media.altText} />
                  ) : (
                    <a href={detail.media.url} target="_blank" rel="noreferrer">
                      {t('mediaOpen')}
                    </a>
                  )}
                </div>
                <form className="auth-form" onSubmit={save}>
                  <Field htmlFor="media-name" label={t('mediaName')} required>
                    <TextField
                      id="media-name"
                      maxLength={128}
                      required
                      value={form.name}
                      onChange={(event) =>
                        setForm({ ...form, name: event.target.value })
                      }
                    />
                  </Field>
                  <Field htmlFor="media-title" label={t('mediaTitle')}>
                    <TextField
                      id="media-title"
                      maxLength={200}
                      value={form.title}
                      onChange={(event) =>
                        setForm({ ...form, title: event.target.value })
                      }
                    />
                  </Field>
                  {detail.media.kind === 'image' && (
                    <>
                      <Field htmlFor="media-alt" label={t('mediaAltText')}>
                        <TextField
                          id="media-alt"
                          maxLength={300}
                          value={form.altText}
                          onChange={(event) =>
                            setForm({ ...form, altText: event.target.value })
                          }
                        />
                      </Field>
                      <p className="content-help">{t('mediaAltHelp')}</p>
                    </>
                  )}
                  <Field htmlFor="media-folder" label={t('mediaFolder')}>
                    <TextField
                      id="media-folder"
                      maxLength={64}
                      value={form.folder}
                      onChange={(event) =>
                        setForm({ ...form, folder: event.target.value })
                      }
                    />
                  </Field>
                  <p className="content-help">{t('mediaFolderHelp')}</p>
                  <label className="content-field" htmlFor="media-markdown">
                    <span>{t('mediaMarkdown')}</span>
                    <input
                      id="media-markdown"
                      readOnly
                      value={detail.media.markdown}
                      onFocus={(event) => event.currentTarget.select()}
                    />
                  </label>
                  <div className="contexts-actions">
                    <Button type="submit" disabled={busy}>
                      {t('mediaSave')}
                    </Button>
                    <Button
                      type="button"
                      variant="secondary"
                      disabled={busy}
                      onClick={() => void remove(false)}
                    >
                      {t('mediaDelete')}
                    </Button>
                  </div>
                </form>
                <section className="media-usages" aria-label={t('mediaUsages')}>
                  <h3>{t('mediaUsages')}</h3>
                  {detail.usages.length === 0 ? (
                    <p>{t('mediaUnused')}</p>
                  ) : (
                    <ul>
                      {detail.usages.map((usage) => (
                        <li key={usage.pageId}>
                          <a
                            href={`/admin/pages/${usage.pageId}`}
                            onClick={(event) => {
                              event.preventDefault();
                              onNavigate({
                                type: 'content-edit',
                                id: usage.pageId,
                              });
                            }}
                          >
                            {usage.title}
                          </a>{' '}
                          <small>
                            /{usage.slug}
                            {usage.isPublished ? '' : ` · ${t('mediaDraft')}`}
                          </small>
                        </li>
                      ))}
                    </ul>
                  )}
                </section>
                {blockingUsages && (
                  <div role="alert" className="media-warning">
                    <p>{t('mediaInUse')}</p>
                    <ul>
                      {blockingUsages.map((usage) => (
                        <li key={usage.pageId}>
                          {usage.title} <small>/{usage.slug}</small>
                        </li>
                      ))}
                    </ul>
                    <Button
                      type="button"
                      variant="secondary"
                      disabled={busy}
                      onClick={() => void remove(true)}
                    >
                      {t('mediaDeleteAnyway')}
                    </Button>
                  </div>
                )}
              </>
            )}
          </Card>
        </div>
      </section>
    </main>
  );
}

/** Compact library shown inside the page editor to insert a media item at the cursor. */
export function MediaPicker({
  t,
  onInsert,
  onSessionExpired,
}: {
  t: Translate;
  onInsert: (media: MediaItem) => void;
  onSessionExpired: () => void;
}) {
  const [filter, setFilter] = useState<MediaFilter>(emptyFilter);
  const {
    items,
    loading,
    error: listError,
    reload,
  } = useMediaList(filter, onSessionExpired);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<api.MediaFailure>();

  const failWith = (failure: api.MediaFailure) => {
    setError(failure);
    if (failure === 'sessionExpired') onSessionExpired();
  };

  const shownError = error ?? listError;

  return (
    <section className="media-picker" aria-label={t('mediaInsert')}>
      <p className="content-help">{t('mediaPickerHelp')}</p>
      <MediaSearch t={t} filter={filter} folders={[]} onChange={setFilter} />
      <UploadButton
        t={t}
        folder={filter.folder}
        busy={busy}
        onBusy={setBusy}
        onError={failWith}
        onUploaded={(uploaded) => {
          setError(undefined);
          void reload();
          onInsert(uploaded.media);
        }}
      />
      {shownError && <p role="alert">{t(mediaFailureKey(shownError))}</p>}
      {loading ? (
        <p role="status">{t('loading')}</p>
      ) : items.length === 0 ? (
        <p>{t('mediaNone')}</p>
      ) : (
        <ul className="media-items">
          {items.map((media) => (
            <li key={media.id}>
              <Thumbnail media={media} />
              <span>
                <strong>{media.title || media.name}</strong>
                <small>
                  {media.name}
                  {media.folder ? ` · ${media.folder}` : ''}
                </small>
              </span>
              <Button
                type="button"
                variant="secondary"
                onClick={() => onInsert(media)}
              >
                {t('mediaInsertAction')}
              </Button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
