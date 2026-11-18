import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Card } from '@manoir-app/core-admin-ui-kit';
import { PageEditor } from './AdminContent';
import * as contextApi from './adminContextApi';
import type { AdminUsageContext } from './adminContextApi';
import * as contentApi from './adminContentApi';
import type { ContentPage } from './adminContentApi';
import * as treeApi from './adminTreeApi';
import type { AdminTreeNode } from './adminTreeApi';
import type { AdminRoute } from './adminRoutes';
import type { TranslationKey } from './translations';

type Translate = (key: TranslationKey) => string;

type View =
  | { type: 'none' }
  | { type: 'page'; pageId: string }
  | {
      type: 'new';
      parentNodeId: string | null;
      parentTitle?: string;
      asRoot: boolean;
    }
  | { type: 'link'; parentNodeId: string; parentTitle: string };

const storageKey = 'homedocs.manual.context';
const defaultContextId = 'maison';

function remembered(): string | null {
  try {
    return window.localStorage.getItem(storageKey);
  } catch {
    return null;
  }
}

function remember(id: string) {
  try {
    window.localStorage.setItem(storageKey, id);
  } catch {
    // The choice is only a convenience.
  }
}

/** The root node of a context. The default context follows the first root of the tree until one is chosen. */
function rootOf(
  context: AdminUsageContext | undefined,
  nodes: AdminTreeNode[],
): AdminTreeNode | undefined {
  if (!context) return undefined;
  if (context.rootNodeId)
    return nodes.find((node) => node.id === context.rootNodeId);
  if (context.id !== defaultContextId) return undefined;
  return nodes
    .filter((node) => node.parentNodeId === null && !node.isLink)
    .sort((a, b) => a.position - b.position || a.id.localeCompare(b.id))[0];
}

function childrenOf(nodes: AdminTreeNode[], parentId: string): AdminTreeNode[] {
  return nodes
    .filter((node) => node.parentNodeId === parentId)
    .sort((a, b) => a.position - b.position || a.id.localeCompare(b.id));
}

function TreeBranch({
  node,
  nodes,
  pages,
  selectedPageId,
  t,
  depth,
  onSelect,
  onAddSubpage,
  onAddLink,
  onRemoveLink,
}: {
  node: AdminTreeNode;
  nodes: AdminTreeNode[];
  pages: Map<string, ContentPage>;
  selectedPageId: string | undefined;
  t: Translate;
  depth: number;
  onSelect: (node: AdminTreeNode) => void;
  onAddSubpage: (node: AdminTreeNode) => void;
  onAddLink: (node: AdminTreeNode) => void;
  onRemoveLink: (node: AdminTreeNode) => void;
}) {
  const page = pages.get(node.pageId);
  const children = node.isLink || depth > 12 ? [] : childrenOf(nodes, node.id);
  return (
    <li>
      <div className="manual-node">
        <button
          type="button"
          className="manual-node-title"
          aria-current={node.pageId === selectedPageId ? 'page' : undefined}
          onClick={() => onSelect(node)}
        >
          <span>{node.title}</span>
          {node.isLink && <small>{t('manualLinkTag')}</small>}
          {page && !page.isPublished && <small>{t('manualDraftTag')}</small>}
        </button>
        <span className="manual-node-actions">
          {!node.isLink && (
            <>
              <Button
                variant="secondary"
                aria-label={`${t('manualAddSubpageTo')} ${node.title}`}
                onClick={() => onAddSubpage(node)}
              >
                + {t('manualAddSubpage')}
              </Button>
              <Button
                variant="secondary"
                aria-label={`${t('manualAddLinkTo')} ${node.title}`}
                onClick={() => onAddLink(node)}
              >
                + {t('manualAddLink')}
              </Button>
            </>
          )}
          {node.isLink && (
            <Button variant="secondary" onClick={() => onRemoveLink(node)}>
              {t('manualRemoveLink')}
            </Button>
          )}
        </span>
      </div>
      {children.length > 0 && (
        <ul>
          {children.map((child) => (
            <TreeBranch
              key={child.id}
              node={child}
              nodes={nodes}
              pages={pages}
              selectedPageId={selectedPageId}
              t={t}
              depth={depth + 1}
              onSelect={onSelect}
              onAddSubpage={onAddSubpage}
              onAddLink={onAddLink}
              onRemoveLink={onRemoveLink}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

function LinkPicker({
  t,
  pages,
  parentTitle,
  busy,
  onPick,
  onCancel,
}: {
  t: Translate;
  pages: ContentPage[];
  parentTitle: string;
  busy: boolean;
  onPick: (page: ContentPage) => void;
  onCancel: () => void;
}) {
  const [query, setQuery] = useState('');
  const needle = query.trim().toLowerCase();
  const shown = pages
    .filter(
      (page) =>
        !needle ||
        page.title.toLowerCase().includes(needle) ||
        page.slug.includes(needle),
    )
    .slice(0, 50);
  return (
    <section className="manual-link-picker">
      <h2>{`${t('manualLinkTitle')} · ${parentTitle}`}</h2>
      <p>{t('manualLinkHelp')}</p>
      <label className="content-field" htmlFor="manual-link-search">
        <span>{t('manualLinkSearch')}</span>
        <input
          id="manual-link-search"
          type="search"
          autoFocus
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
      </label>
      {shown.length === 0 ? (
        <p>{t('manualLinkNone')}</p>
      ) : (
        <ul className="manual-link-results">
          {shown.map((page) => (
            <li key={page.id}>
              <span>
                <strong>{page.title}</strong>
                <small>/{page.slug}</small>
              </span>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => onPick(page)}
              >
                {t('manualLinkAction')}
              </Button>
            </li>
          ))}
        </ul>
      )}
      <Button variant="secondary" onClick={onCancel}>
        {t('manualCancel')}
      </Button>
    </section>
  );
}

export function AdminManual({
  translate: t,
  onNavigate,
  onSessionExpired,
}: {
  translate: Translate;
  onNavigate: (route: AdminRoute) => void;
  onSessionExpired: () => void;
}) {
  const [contexts, setContexts] = useState<AdminUsageContext[]>([]);
  const [nodes, setNodes] = useState<AdminTreeNode[]>([]);
  const [pages, setPages] = useState<ContentPage[]>([]);
  const [contextId, setContextId] = useState<string>();
  const [view, setView] = useState<View>({ type: 'none' });
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<TranslationKey>();
  const requestId = useRef(0);

  const fail = useCallback(
    (reason: unknown) => {
      const code =
        reason instanceof treeApi.TreeError ||
        reason instanceof contextApi.ContextError ||
        reason instanceof contentApi.ContentError
          ? reason.reason
          : 'unavailable';
      if (code === 'sessionExpired') onSessionExpired();
      setFailed(true);
    },
    [onSessionExpired],
  );

  const load = useCallback(async () => {
    const id = ++requestId.current;
    try {
      const [loadedContexts, loadedNodes, loadedPages] = await Promise.all([
        contextApi.listContexts(),
        treeApi.listTree(),
        contentApi.listPages(),
      ]);
      if (id !== requestId.current) return;
      setContexts(loadedContexts);
      setNodes(loadedNodes);
      setPages(loadedPages);
      setFailed(false);
      setContextId((current) => {
        if (current && loadedContexts.some((item) => item.id === current))
          return current;
        const saved = remembered();
        return (
          loadedContexts.find((item) => item.id === saved)?.id ??
          loadedContexts.find((item) => item.id === defaultContextId)?.id ??
          loadedContexts[0]?.id
        );
      });
    } catch (reason) {
      if (id === requestId.current) fail(reason);
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [fail]);

  useEffect(() => {
    const active = requestId;
    queueMicrotask(() => void load());
    return () => {
      active.current++;
    };
  }, [load]);

  const context = contexts.find((item) => item.id === contextId);
  const root = rootOf(context, nodes);
  const pageMap = new Map(pages.map((page) => [page.id, page]));
  const selectedPageId = view.type === 'page' ? view.pageId : undefined;

  const chooseContext = (id: string) => {
    setContextId(id);
    remember(id);
    setView({ type: 'none' });
    setNotice(undefined);
  };

  const created = async (pageId: string) => {
    const asRoot = view.type === 'new' && view.asRoot;
    try {
      const freshNodes = await treeApi.listTree();
      const node = freshNodes.find(
        (item) =>
          item.pageId === pageId && !item.isLink && item.parentNodeId === null,
      );
      // The first page of a context becomes its root, so that the public manual opens on it.
      if (asRoot && context && node)
        await contextApi.updateContext(context.id, {
          ...context,
          rootNodeId: node.id,
        });
    } catch (reason) {
      fail(reason);
    }
    await load();
    setView({ type: 'page', pageId });
    setNotice('manualPageCreated');
  };

  const addLink = async (page: ContentPage) => {
    if (view.type !== 'link') return;
    setBusy(true);
    try {
      await treeApi.createLink(page.id, view.parentNodeId);
      await load();
      setView({ type: 'none' });
      setNotice('manualLinkCreated');
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const removeLink = async (node: AdminTreeNode) => {
    if (!window.confirm(t('manualRemoveLinkConfirm'))) return;
    setBusy(true);
    try {
      await treeApi.removeNode(node.id);
      await load();
      if (selectedPageId === node.pageId) setView({ type: 'none' });
    } catch (reason) {
      fail(reason);
    } finally {
      setBusy(false);
    }
  };

  const editorNavigate = (route: AdminRoute) => {
    if (route?.type === 'content-edit')
      setView({ type: 'page', pageId: route.id });
    else {
      setView({ type: 'none' });
      void load();
    }
  };

  return (
    <main className="mn-kit-theme content-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={() => onNavigate(null)}>
          {t('backToAdmin')}
        </Button>
      </header>
      <section className="manual-content">
        <div className="content-heading">
          <div>
            <h1>{t('manualTitle')}</h1>
            <p>{t('manualDescription')}</p>
          </div>
        </div>
        {failed && <p role="alert">{t('manualUnavailable')}</p>}
        {notice && <p role="status">{t(notice)}</p>}
        {loading && <p role="status">{t('loading')}</p>}
        {!loading && contexts.length === 0 && (
          <Card>
            <p>{t('manualNoContext')}</p>
            <Button onClick={() => onNavigate({ type: 'contexts' })}>
              {t('openContexts')}
            </Button>
          </Card>
        )}
        {contexts.length > 0 && (
          <div className="manual-toolbar">
            <label className="content-field" htmlFor="manual-context">
              <span>{t('manualContext')}</span>
              <select
                id="manual-context"
                value={contextId ?? ''}
                onChange={(event) => chooseContext(event.target.value)}
              >
                {contexts.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            {context && (
              <a
                href={`/?context=${encodeURIComponent(context.id)}&profile=desktop`}
                target="_blank"
                rel="noreferrer"
              >
                {t('manualViewContext')}
              </a>
            )}
            <Button
              variant="secondary"
              onClick={() => onNavigate({ type: 'contexts' })}
            >
              {t('openContexts')}
            </Button>
          </div>
        )}
        {context && (
          <div className="manual-layout">
            <div className="manual-main">
              {view.type === 'none' && <p>{t('manualSelectPage')}</p>}
              {view.type === 'page' && (
                <PageEditor
                  key={view.pageId}
                  pageId={view.pageId}
                  t={t}
                  embedded
                  onNavigate={editorNavigate}
                  onSessionExpired={onSessionExpired}
                  onChanged={() => void load()}
                />
              )}
              {view.type === 'new' && (
                <>
                  <p className="content-state">
                    {view.parentTitle
                      ? `${t('manualNewSubpageOf')} ${view.parentTitle}`
                      : t('manualNewRoot')}
                  </p>
                  <PageEditor
                    key={`new-${view.parentNodeId ?? 'root'}`}
                    t={t}
                    embedded
                    fixedParent={{ nodeId: view.parentNodeId }}
                    onNavigate={editorNavigate}
                    onSessionExpired={onSessionExpired}
                    onCreated={(id) => void created(id)}
                  />
                </>
              )}
              {view.type === 'link' && (
                <LinkPicker
                  t={t}
                  pages={pages}
                  parentTitle={view.parentTitle}
                  busy={busy}
                  onPick={(page) => void addLink(page)}
                  onCancel={() => setView({ type: 'none' })}
                />
              )}
            </div>
            <aside className="manual-tree" aria-label={t('manualTreeTitle')}>
              <h2>{`${t('manualTreeTitle')} · ${context.name}`}</h2>
              {root && pageMap.get(root.pageId)?.isPublished === false && (
                <p className="manual-warning" role="note">
                  {t('manualRootDraft')}
                </p>
              )}
              {root ? (
                <ul>
                  <TreeBranch
                    node={root}
                    nodes={nodes}
                    pages={pageMap}
                    selectedPageId={selectedPageId}
                    t={t}
                    depth={0}
                    onSelect={(node) => {
                      setNotice(undefined);
                      setView({ type: 'page', pageId: node.pageId });
                    }}
                    onAddSubpage={(node) => {
                      setNotice(undefined);
                      setView({
                        type: 'new',
                        parentNodeId: node.id,
                        parentTitle: node.title,
                        asRoot: false,
                      });
                    }}
                    onAddLink={(node) => {
                      setNotice(undefined);
                      setView({
                        type: 'link',
                        parentNodeId: node.id,
                        parentTitle: node.title,
                      });
                    }}
                    onRemoveLink={(node) => void removeLink(node)}
                  />
                </ul>
              ) : (
                <>
                  <p>{t('manualNoRoot')}</p>
                  <Button
                    onClick={() => {
                      setNotice(undefined);
                      setView({
                        type: 'new',
                        parentNodeId: null,
                        asRoot: true,
                      });
                    }}
                  >
                    {t('manualCreateRoot')}
                  </Button>
                </>
              )}
              <Button
                variant="secondary"
                onClick={() => onNavigate({ type: 'tree' })}
              >
                {t('manualReorder')}
              </Button>
            </aside>
          </div>
        )}
      </section>
    </main>
  );
}
