import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import CodeMirror from '@uiw/react-codemirror';
import { markdown, markdownLanguage } from '@codemirror/lang-markdown';
import { languages } from '@codemirror/language-data';
import { EditorView } from '@codemirror/view';
import { Button, Card, Field, TextField } from '@manoir-app/core-admin-ui-kit';
import ReactMarkdown from 'react-markdown';
import { MediaPicker } from './AdminMedia';
import * as api from './adminContentApi';
import type { ContentPage, ContentPageInput } from './adminContentApi';
import * as contextApi from './adminContextApi';
import type { AdminUsageContext } from './adminContextApi';
import type { MediaItem } from './adminMediaApi';
import * as treeApi from './adminTreeApi';
import type { AdminTreeNode } from './adminTreeApi';
import type { AdminRoute } from './adminRoutes';
import type { TranslationKey } from './translations';

interface Props {
  route: Exclude<
    AdminRoute,
    { type: 'settings' | 'tree' | 'contexts' | 'media' } | null
  >;
  translate: (key: TranslationKey) => string;
  onNavigate: (route: AdminRoute) => void;
  onSessionExpired: () => void;
}

const categoryKeys = [
  ['page', 'categoryPage'],
  ['piece', 'categoryPiece'],
  ['emplacement', 'categoryLocation'],
  ['procedure', 'categoryProcedure'],
  ['equipement', 'categoryEquipment'],
  ['depannage', 'categoryTroubleshooting'],
  ['technique', 'categoryTechnical'],
] as const;

const emptyPage: ContentPageInput = {
  title: '',
  slug: '',
  category: 'page',
  contexts: [],
  requiresAuthentication: false,
  markdown: '',
};

function failureKey(error: api.ContentFailure): TranslationKey {
  if (error === 'invalid') return 'contentInvalid';
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'notFound') return 'contentNotFound';
  if (error === 'conflict') return 'contentConflict';
  if (error === 'throttled') return 'contentRateLimit';
  return 'contentUnavailable';
}

export function AdminContent({
  route,
  translate: t,
  onNavigate,
  onSessionExpired,
}: Props) {
  return (
    <main className="mn-kit-theme content-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={() => onNavigate(null)}>
          {t('backToAdmin')}
        </Button>
      </header>
      {route.type === 'content-list' ? (
        <PageList
          t={t}
          onNavigate={onNavigate}
          onSessionExpired={onSessionExpired}
        />
      ) : (
        <PageEditor
          pageId={route.type === 'content-edit' ? route.id : undefined}
          t={t}
          onNavigate={onNavigate}
          onSessionExpired={onSessionExpired}
        />
      )}
    </main>
  );
}

function PageList({
  t,
  onNavigate,
  onSessionExpired,
}: {
  t: Props['translate'];
  onNavigate: Props['onNavigate'];
  onSessionExpired: Props['onSessionExpired'];
}) {
  const [pages, setPages] = useState<ContentPage[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<api.ContentFailure>();
  const revision = useRef(0);

  const load = useCallback(async () => {
    const requestId = ++revision.current;
    setLoading(true);
    setError(undefined);
    try {
      const result = await api.listPages();
      if (requestId === revision.current) setPages(result);
    } catch (reason) {
      if (requestId === revision.current) {
        const failure =
          reason instanceof api.ContentError ? reason.reason : 'unavailable';
        setError(failure);
        if (failure === 'sessionExpired') onSessionExpired();
      }
    } finally {
      if (requestId === revision.current) setLoading(false);
    }
  }, [onSessionExpired]);

  useEffect(() => {
    const activeRevision = revision;
    let active = true;
    queueMicrotask(() => {
      if (active) void load();
    });
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [load]);

  return (
    <section className="content-list">
      <div className="content-heading">
        <div>
          <h1>{t('pages')}</h1>
          <p>{t('editorDescription')}</p>
        </div>
        <Button onClick={() => onNavigate({ type: 'content-new' })}>
          {t('createPage')}
        </Button>
        <Button
          variant="secondary"
          onClick={() => onNavigate({ type: 'tree' })}
        >
          {t('openTree')}
        </Button>
      </div>
      {error && <p role="alert">{t(failureKey(error))}</p>}
      {loading ? (
        <p role="status">{t('loading')}</p>
      ) : pages.length === 0 ? (
        <Card className="content-empty">
          <p>{t('noPages')}</p>
        </Card>
      ) : (
        <ul className="content-list-items">
          {pages.map((page) => (
            <li key={page.id}>
              <button
                type="button"
                className="content-list-link"
                onClick={() =>
                  onNavigate({ type: 'content-edit', id: page.id })
                }
              >
                <span>
                  <strong>{page.title}</strong>
                  <small>/{page.slug}</small>
                </span>
                <span className="content-list-status">
                  {t(page.isPublished ? 'pagePublished' : 'pageDraft')}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {error && (
        <Button variant="secondary" onClick={() => void load()}>
          {t('retry')}
        </Button>
      )}
    </section>
  );
}

function PageEditor({
  pageId,
  t,
  onNavigate,
  onSessionExpired,
}: {
  pageId?: string;
  t: Props['translate'];
  onNavigate: Props['onNavigate'];
  onSessionExpired: Props['onSessionExpired'];
}) {
  const [page, setPage] = useState<ContentPage>();
  const [form, setForm] = useState<ContentPageInput>(emptyPage);
  const [contexts, setContexts] = useState<AdminUsageContext[]>([]);
  const [contextsLoading, setContextsLoading] = useState(true);
  const [treeNodes, setTreeNodes] = useState<AdminTreeNode[]>([]);
  const [placementChoice, setPlacementChoice] = useState('');
  const [placementLoading, setPlacementLoading] = useState(!pageId);
  const [preview, setPreview] = useState(false);
  const [mediaOpen, setMediaOpen] = useState(false);
  const editorView = useRef<EditorView | null>(null);
  const [loading, setLoading] = useState(Boolean(pageId));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<api.ContentFailure>();
  const [notice, setNotice] = useState<TranslationKey>();
  const revision = useRef(0);
  const contextsRevision = useRef(0);

  const reload = useCallback(async () => {
    if (!pageId) return;
    const requestId = ++revision.current;
    setLoading(true);
    setError(undefined);
    try {
      const result = await api.getPage(pageId);
      if (requestId === revision.current) {
        setPage(result);
        setForm({
          title: result.title,
          slug: result.slug,
          category: result.category,
          contexts: result.contexts,
          requiresAuthentication: result.requiresAuthentication,
          markdown: result.markdown,
        });
      }
    } catch (reason) {
      if (requestId === revision.current) {
        const failure =
          reason instanceof api.ContentError ? reason.reason : 'unavailable';
        setError(failure);
        if (failure === 'sessionExpired') onSessionExpired();
      }
    } finally {
      if (requestId === revision.current) setLoading(false);
    }
  }, [onSessionExpired, pageId]);

  useEffect(() => {
    const activeRevision = revision;
    let active = true;
    if (pageId) {
      queueMicrotask(() => {
        if (active) void reload();
      });
    }
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [pageId, reload]);

  useEffect(() => {
    const activeRevision = contextsRevision;
    let active = true;
    queueMicrotask(() => {
      if (!active) return;
      const requestId = ++activeRevision.current;
      void contextApi
        .listContexts()
        .then((result) => {
          if (requestId === activeRevision.current) setContexts(result);
        })
        .catch((reason) => {
          if (requestId !== activeRevision.current) return;
          const contextFailure =
            reason instanceof contextApi.ContextError
              ? reason.reason
              : 'unavailable';
          const failure =
            contextFailure === 'invalidRoot' ? 'invalid' : contextFailure;
          setError(failure);
          if (failure === 'sessionExpired') onSessionExpired();
        })
        .finally(() => {
          if (requestId === activeRevision.current) setContextsLoading(false);
        });
    });
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [onSessionExpired]);

  useEffect(() => {
    const activeRevision = revision;
    let active = true;
    if (!pageId) {
      queueMicrotask(() => {
        if (!active) return;
        const requestId = ++activeRevision.current;
        void treeApi
          .listTree()
          .then((nodes) => {
            if (requestId === activeRevision.current) setTreeNodes(nodes);
          })
          .catch((reason) => {
            if (requestId !== activeRevision.current) return;
            const failure =
              reason instanceof treeApi.TreeError
                ? reason.reason
                : 'unavailable';
            setError(failure);
            if (failure === 'sessionExpired') onSessionExpired();
          })
          .finally(() => {
            if (requestId === activeRevision.current)
              setPlacementLoading(false);
          });
      });
    }
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [onSessionExpired, pageId]);

  const update = <K extends keyof ContentPageInput>(
    key: K,
    value: ContentPageInput[K],
  ) => {
    setForm((current) => ({ ...current, [key]: value }));
    setError(undefined);
    setNotice(undefined);
  };

  const insertMedia = (media: MediaItem) => {
    const view = editorView.current;
    if (view?.dom.isConnected) {
      view.dispatch(view.state.replaceSelection(`${media.markdown}\n`));
      view.focus();
    } else {
      setForm((current) => ({
        ...current,
        markdown: `${current.markdown.trimEnd()}${current.markdown.trim() ? '\n\n' : ''}${media.markdown}\n`,
      }));
    }
    setError(undefined);
    setNotice('mediaInserted');
  };

  const toPayload = (): ContentPageInput => ({
    ...form,
    title: form.title.trim(),
    contexts: form.contexts,
  });

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      const payload = toPayload();
      if (page) {
        await api.updatePage(page, payload);
        const updated = await api.getPage(page.id);
        setPage(updated);
        setForm({ ...payload, contexts: updated.contexts });
        setNotice('pageSaved');
      } else {
        if (!placementChoice) {
          setError('invalid');
          return;
        }
        const parentNodeId =
          placementChoice === 'root' ? null : placementChoice;
        const id = await api.createPage(payload, parentNodeId);
        onNavigate({ type: 'content-edit', id });
      }
    } catch (reason) {
      const failure =
        reason instanceof api.ContentError ? reason.reason : 'unavailable';
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      setBusy(false);
    }
  };

  const togglePublication = async () => {
    if (!page) return;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      await api.setPublication(page, !page.isPublished);
      setPage({
        ...page,
        isPublished: !page.isPublished,
        revision: page.revision + 1,
      });
      setNotice(
        page.isPublished ? 'pageUnpublishedNotice' : 'pagePublishedNotice',
      );
    } catch (reason) {
      const failure =
        reason instanceof api.ContentError ? reason.reason : 'unavailable';
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!page || !window.confirm(t('confirmDeletePage'))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api.deletePage(page);
      onNavigate({ type: 'content-list' });
    } catch (reason) {
      const failure =
        reason instanceof api.ContentError ? reason.reason : 'unavailable';
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      setBusy(false);
    }
  };

  if (loading)
    return (
      <p className="admin-status" role="status">
        {t('loading')}
      </p>
    );
  if (pageId && !page) {
    return (
      <section className="content-editor">
        <p role="alert">
          {error ? t(failureKey(error)) : t('contentUnavailable')}
        </p>
        <Button variant="secondary" onClick={() => void reload()}>
          {t('retry')}
        </Button>
      </section>
    );
  }

  return (
    <section className="content-editor">
      <div className="content-heading">
        <div>
          <h1>{page ? t('editPage') : t('createPage')}</h1>
          <p>{t('editorDescription')}</p>
        </div>
        <Button
          variant="secondary"
          onClick={() => onNavigate({ type: 'content-list' })}
        >
          {t('pages')}
        </Button>
        <Button
          variant="secondary"
          onClick={() => onNavigate({ type: 'tree' })}
        >
          {t('tree')}
        </Button>
      </div>
      {page && (
        <p className="content-state">
          {t(page.isPublished ? 'pagePublished' : 'pageDraft')}
          {` · v${page.revision}`}
        </p>
      )}
      {error && <p role="alert">{t(failureKey(error))}</p>}
      {notice && <p role="status">{t(notice)}</p>}
      <form className="content-editor-form" onSubmit={save}>
        <div className="content-metadata">
          <Field htmlFor="page-title" label={t('pageTitle')} required>
            <TextField
              id="page-title"
              maxLength={200}
              required
              value={form.title}
              onChange={(event) => update('title', event.target.value)}
            />
          </Field>
          <Field htmlFor="page-slug" label={t('slug')} required>
            <TextField
              id="page-slug"
              maxLength={80}
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
              required
              value={form.slug}
              onChange={(event) => update('slug', event.target.value)}
            />
          </Field>
          <label className="content-field" htmlFor="page-category">
            <span>{t('category')}</span>
            <select
              id="page-category"
              required
              value={form.category}
              onChange={(event) => update('category', event.target.value)}
            >
              {categoryKeys.map(([value, key]) => (
                <option key={value} value={value}>
                  {t(key)}
                </option>
              ))}
            </select>
          </label>
          <fieldset className="content-contexts" disabled={contextsLoading}>
            <legend>{t('availableContexts')}</legend>
            <p className="content-help">{t('universalContext')}</p>
            {contexts.map((usageContext) => (
              <label key={usageContext.id} className="content-checkbox">
                <input
                  type="checkbox"
                  checked={form.contexts.includes(usageContext.id)}
                  onChange={(event) =>
                    update(
                      'contexts',
                      event.target.checked
                        ? [...form.contexts, usageContext.id]
                        : form.contexts.filter(
                            (item) => item !== usageContext.id,
                          ),
                    )
                  }
                />
                <span>
                  {usageContext.name} ({usageContext.id})
                </span>
              </label>
            ))}
          </fieldset>
          {!page && (
            <label className="content-field" htmlFor="page-placement">
              <span>{t('placement')}</span>
              <select
                id="page-placement"
                required
                disabled={placementLoading || Boolean(error)}
                value={placementChoice}
                onChange={(event) => setPlacementChoice(event.target.value)}
              >
                <option value="">{t('choosePlacement')}</option>
                <option value="root">{t('root')}</option>
                {treeNodes.map((node) => (
                  <option key={node.id} value={node.id}>
                    {`${node.title} · /${node.slug}${node.isLink ? ` · ${t('linkNode')}` : ''}`}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="content-checkbox">
            <input
              type="checkbox"
              checked={form.requiresAuthentication}
              onChange={(event) =>
                update('requiresAuthentication', event.target.checked)
              }
            />
            <span>{t('requiresAuthentication')}</span>
          </label>
          <p className="content-help">{t('pinEnforcementDeferred')}</p>
        </div>
        <div className="content-editor-toolbar">
          <h2>{t(preview ? 'preview' : 'markdown')}</h2>
          <div className="content-editor-tools">
            <Button
              type="button"
              variant="secondary"
              aria-expanded={mediaOpen}
              onClick={() => setMediaOpen((current) => !current)}
            >
              {t(mediaOpen ? 'mediaInsertClose' : 'mediaInsert')}
            </Button>
            <Button
              type="button"
              variant="secondary"
              aria-pressed={preview}
              onClick={() => setPreview((current) => !current)}
            >
              {t(preview ? 'markdown' : 'preview')}
            </Button>
          </div>
        </div>
        {mediaOpen && (
          <MediaPicker
            t={t}
            onInsert={insertMedia}
            onSessionExpired={onSessionExpired}
          />
        )}
        {preview ? (
          <div className="markdown-preview">
            <ReactMarkdown skipHtml>{form.markdown}</ReactMarkdown>
          </div>
        ) : (
          <CodeMirror
            value={form.markdown}
            height="24rem"
            aria-label={t('markdownEditor')}
            extensions={[
              EditorView.contentAttributes.of({
                'aria-label': t('markdownEditor'),
              }),
              markdown({
                base: markdownLanguage,
                codeLanguages: languages,
              }),
            ]}
            onChange={(markdownValue) => update('markdown', markdownValue)}
            onCreateEditor={(view) => {
              editorView.current = view;
            }}
          />
        )}
        <div className="content-actions">
          <Button
            type="submit"
            disabled={busy || contextsLoading || Boolean(error)}
          >
            {t('savePage')}
          </Button>
          {page && (
            <>
              <Button
                type="button"
                variant="secondary"
                disabled={busy}
                onClick={() => void togglePublication()}
              >
                {t(page.isPublished ? 'unpublishPage' : 'publishPage')}
              </Button>
              <Button
                type="button"
                variant="secondary"
                disabled={busy}
                onClick={() => void remove()}
              >
                {t('deletePage')}
              </Button>
            </>
          )}
        </div>
      </form>
    </section>
  );
}
