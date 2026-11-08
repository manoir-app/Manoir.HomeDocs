import { useCallback, useEffect, useRef, useState } from 'react';
import { Button } from '@manoir-app/core-admin-ui-kit';
import * as api from './adminHistoryApi';
import type { LinkReport, Version, VersionSummary } from './adminHistoryApi';
import type { TranslationKey } from './translations';

type Translate = (key: TranslationKey) => string;

const eventKeys: Record<string, TranslationKey> = {
  created: 'historyCreated',
  edited: 'historyEdited',
  published: 'historyPublished',
  unpublished: 'historyUnpublished',
  restored: 'historyRestored',
  baseline: 'historyBaseline',
};

const statusKeys: Record<string, TranslationKey> = {
  ok: 'linkOk',
  redirected: 'linkRedirected',
  draft: 'linkDraft',
  broken: 'linkBroken',
  unchecked: 'linkUnchecked',
};

const kindKeys: Record<string, TranslationKey> = {
  page: 'linkKindPage',
  media: 'linkKindMedia',
  external: 'linkKindExternal',
};

function failureKey(error: api.HistoryFailure): TranslationKey {
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'conflict') return 'historyConflict';
  if (error === 'notFound') return 'historyNotFound';
  if (error === 'invalid') return 'historyInvalid';
  return 'historyUnavailable';
}

function failureOf(reason: unknown): api.HistoryFailure {
  return reason instanceof api.HistoryError ? reason.reason : 'unavailable';
}

/** Versions of a page, with the Markdown of each and a one-click restore. */
export function PageHistoryPanel({
  t,
  pageId,
  revision,
  onRestored,
  onSessionExpired,
}: {
  t: Translate;
  pageId: string;
  revision: number;
  onRestored: () => void;
  onSessionExpired: () => void;
}) {
  const [versions, setVersions] = useState<VersionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<api.HistoryFailure>();
  const [shown, setShown] = useState<Version>();
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState(false);
  const requestId = useRef(0);

  const fail = useCallback(
    (reason: unknown) => {
      const failure = failureOf(reason);
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    },
    [onSessionExpired],
  );

  const load = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    try {
      const result = await api.listVersions(pageId);
      if (id !== requestId.current) return;
      setVersions(result);
      setError(undefined);
    } catch (reason) {
      if (id === requestId.current) fail(reason);
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [pageId, fail]);

  useEffect(() => {
    const current = requestId;
    let active = true;
    queueMicrotask(() => {
      if (active) void load();
    });
    return () => {
      active = false;
      current.current++;
    };
  }, [load, revision]);

  const show = async (summary: VersionSummary) => {
    if (shown?.summary.revision === summary.revision) {
      setShown(undefined);
      return;
    }
    try {
      setShown(await api.getVersion(pageId, summary.revision));
    } catch (reason) {
      fail(reason);
    }
  };

  const restore = async (summary: VersionSummary) => {
    if (!window.confirm(t('historyRestoreConfirm'))) return;
    setBusy(true);
    setError(undefined);
    setNotice(false);
    try {
      await api.restoreVersion(pageId, revision, summary.revision);
      setShown(undefined);
      setNotice(true);
      onRestored();
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="history-panel" aria-label={t('history')}>
      <h2>{t('history')}</h2>
      <p className="content-help">{t('historyHelp')}</p>
      {error && <p role="alert">{t(failureKey(error))}</p>}
      {notice && <p role="status">{t('historyRestoredNotice')}</p>}
      {loading ? (
        <p role="status">{t('loading')}</p>
      ) : versions.length === 0 ? (
        <p>{t('historyEmpty')}</p>
      ) : (
        <ul className="history-list">
          {versions.map((version, index) => (
            <li key={version.revision}>
              <div>
                <strong>
                  {t('historyVersion')} {version.revision}
                </strong>{' '}
                · {t(eventKeys[version.event] ?? 'historyEdited')}
                {version.restoredFromRevision !== null &&
                  ` (${t('historyFromVersion')} ${version.restoredFromRevision})`}
                <br />
                <small>
                  {new Date(version.savedAt).toLocaleString()} · {version.title}{' '}
                  · /{version.slug}
                </small>
              </div>
              <div className="history-actions">
                <Button
                  type="button"
                  variant="secondary"
                  aria-expanded={shown?.summary.revision === version.revision}
                  onClick={() => void show(version)}
                >
                  {t(
                    shown?.summary.revision === version.revision
                      ? 'historyHide'
                      : 'historyShow',
                  )}
                </Button>
                {index > 0 && (
                  <Button
                    type="button"
                    variant="secondary"
                    disabled={busy}
                    onClick={() => void restore(version)}
                  >
                    {t('historyRestore')}
                  </Button>
                )}
              </div>
              {shown?.summary.revision === version.revision && (
                <pre className="history-markdown">{shown.markdown}</pre>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** Outgoing links with their status, and the pages that link here. */
export function PageLinksPanel({
  t,
  pageId,
  revision,
  onNavigatePage,
  onSessionExpired,
}: {
  t: Translate;
  pageId: string;
  revision: number;
  onNavigatePage: (id: string) => void;
  onSessionExpired: () => void;
}) {
  const [report, setReport] = useState<LinkReport>();
  const [error, setError] = useState<api.HistoryFailure>();
  const requestId = useRef(0);

  useEffect(() => {
    const current = requestId;
    const id = ++current.current;
    queueMicrotask(() => {
      api
        .getLinks(pageId)
        .then((result) => {
          if (id !== current.current) return;
          setReport(result);
          setError(undefined);
        })
        .catch((reason: unknown) => {
          if (id !== current.current) return;
          const failure = failureOf(reason);
          setError(failure);
          if (failure === 'sessionExpired') onSessionExpired();
        });
    });
    return () => {
      current.current++;
    };
  }, [pageId, revision, onSessionExpired]);

  const open = (id: string) => (event: { preventDefault: () => void }) => {
    event.preventDefault();
    onNavigatePage(id);
  };

  return (
    <section className="links-panel" aria-label={t('links')}>
      <h2>{t('links')}</h2>
      {error && <p role="alert">{t(failureKey(error))}</p>}
      {report && (
        <>
          <h3>{t('linksOutgoing')}</h3>
          {report.outgoing.length === 0 ? (
            <p>{t('linksNone')}</p>
          ) : (
            <ul className="links-list">
              {report.outgoing.map((link) => (
                <li key={link.href} data-status={link.status}>
                  <code>{link.href}</code> ·{' '}
                  {t(kindKeys[link.kind] ?? 'linkKindPage')} ·{' '}
                  <strong>{t(statusKeys[link.status] ?? 'linkBroken')}</strong>
                  {link.targetTitle && link.kind === 'page' && link.targetId ? (
                    <>
                      {' '}
                      →{' '}
                      <a
                        href={`/admin/pages/${link.targetId}`}
                        onClick={open(link.targetId)}
                      >
                        {link.targetTitle}
                      </a>
                    </>
                  ) : link.targetTitle ? (
                    ` → ${link.targetTitle}`
                  ) : null}
                </li>
              ))}
            </ul>
          )}
          <h3>{t('linksIncoming')}</h3>
          {report.incoming.length === 0 ? (
            <p>{t('linksNoIncoming')}</p>
          ) : (
            <ul className="links-list">
              {report.incoming.map((link) => (
                <li key={link.pageId}>
                  <a
                    href={`/admin/pages/${link.pageId}`}
                    onClick={open(link.pageId)}
                  >
                    {link.title}
                  </a>{' '}
                  <small>
                    /{link.slug}
                    {link.isPublished ? '' : ` · ${t('mediaDraft')}`}
                  </small>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
