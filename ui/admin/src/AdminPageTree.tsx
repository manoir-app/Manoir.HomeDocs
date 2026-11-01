import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Card } from '@manoir-app/core-admin-ui-kit';
import * as contentApi from './adminContentApi';
import * as treeApi from './adminTreeApi';
import type { AdminTreeNode, TreeFailure } from './adminTreeApi';
import type { AdminRoute } from './adminRoutes';
import type { TranslationKey } from './translations';

interface Props {
  translate: (key: TranslationKey) => string;
  onNavigate: (route: AdminRoute) => void;
  onSessionExpired: () => void;
}

function failureKey(error: TreeFailure): TranslationKey {
  if (error === 'invalid') return 'treeInvalid';
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'notFound') return 'treeNotFound';
  if (error === 'conflict') return 'treeConflict';
  return 'treeUnavailable';
}

export function AdminPageTree({
  translate: t,
  onNavigate,
  onSessionExpired,
}: Props) {
  const [nodes, setNodes] = useState<AdminTreeNode[]>([]);
  const [pages, setPages] = useState<contentApi.ContentPage[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<TreeFailure>();
  const [notice, setNotice] = useState(false);
  const [linkTarget, setLinkTarget] = useState('');
  const [linkParent, setLinkParent] = useState('');
  const draggedNode = useRef<string | null>(null);
  const requestRevision = useRef(0);

  const reload = useCallback(async (): Promise<boolean> => {
    const requestId = ++requestRevision.current;
    setLoading(true);
    setError(undefined);
    try {
      const [treeItems, allPages] = await Promise.all([
        treeApi.listTree(),
        contentApi.listPages(),
      ]);
      if (requestId === requestRevision.current) {
        setNodes(treeItems);
        setPages(allPages);
        setLinkTarget((current) =>
          current && allPages.some((page) => page.id === current)
            ? current
            : (allPages[0]?.id ?? ''),
        );
      }
      return true;
    } catch (reason) {
      if (requestId === requestRevision.current) {
        const failure =
          reason instanceof treeApi.TreeError
            ? reason.reason
            : reason instanceof contentApi.ContentError
              ? mapContentFailure(reason.reason)
              : 'unavailable';
        setError(failure);
        if (failure === 'sessionExpired') onSessionExpired();
      }
      return false;
    } finally {
      if (requestId === requestRevision.current) setLoading(false);
    }
  }, [onSessionExpired]);

  useEffect(() => {
    const activeRevision = requestRevision;
    let active = true;
    queueMicrotask(() => {
      if (active) void reload();
    });
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [reload]);

  const mutate = async (operation: () => Promise<void>) => {
    setBusy(true);
    setError(undefined);
    setNotice(false);
    try {
      await operation();
      if (await reload()) setNotice(true);
    } catch (reason) {
      const failure =
        reason instanceof treeApi.TreeError ? reason.reason : 'unavailable';
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      setBusy(false);
    }
  };

  const createSelectedLink = () => {
    if (!linkTarget || !linkParent) return;
    void mutate(() =>
      treeApi.createLink(linkTarget, linkParent === 'root' ? null : linkParent),
    );
  };

  const moveNode = (
    node: AdminTreeNode,
    parentNodeId: string | null,
    position: number,
  ) => mutate(() => treeApi.moveNode(node.id, parentNodeId, position));

  const removeNode = (node: AdminTreeNode) => {
    if (!window.confirm(t('confirmRemoveNode'))) return;
    void mutate(() => treeApi.removeNode(node.id));
  };

  const renderChildren = (parentNodeId: string | null): React.ReactNode => {
    const siblings = nodes
      .filter((node) => node.parentNodeId === parentNodeId)
      .sort(
        (left, right) =>
          left.position - right.position || left.id.localeCompare(right.id),
      );
    if (siblings.length === 0) return null;

    return (
      <ul
        role={parentNodeId === null ? 'tree' : 'group'}
        className="page-tree-list"
      >
        {siblings.map((node, index) => {
          const previousSibling = siblings[index - 1];
          const parent = node.parentNodeId
            ? nodes.find((item) => item.id === node.parentNodeId)
            : undefined;
          const grandparents = parent?.parentNodeId ?? null;
          const grandparentSiblings = nodes
            .filter((item) => item.parentNodeId === grandparents)
            .sort((left, right) => left.position - right.position);
          const parentIndex = parent
            ? grandparentSiblings.findIndex((item) => item.id === parent.id)
            : -1;

          return (
            <li
              key={node.id}
              role="treeitem"
              aria-expanded={
                nodes.some((item) => item.parentNodeId === node.id) || undefined
              }
              draggable={!busy}
              onDragStart={() => {
                draggedNode.current = node.id;
              }}
              onDragOver={(event) => event.preventDefault()}
              onDrop={(event) => {
                event.preventDefault();
                const source = nodes.find(
                  (item) => item.id === draggedNode.current,
                );
                draggedNode.current = null;
                if (!source || source.id === node.id) return;
                const adjustedPosition =
                  source.parentNodeId === node.parentNodeId &&
                  source.position < node.position
                    ? node.position - 1
                    : node.position;
                void moveNode(source, node.parentNodeId, adjustedPosition);
              }}
            >
              <div className="page-tree-row">
                <button
                  type="button"
                  className="page-tree-target"
                  onClick={() =>
                    onNavigate({ type: 'content-edit', id: node.pageId })
                  }
                >
                  <strong>{node.title}</strong>
                  {node.isLink && <small>{t('linkNode')}</small>}
                </button>
                <div className="page-tree-actions">
                  <Button
                    type="button"
                    variant="secondary"
                    aria-label={`${t('moveUp')}: ${node.title}`}
                    disabled={busy || index === 0}
                    onClick={() =>
                      void moveNode(node, node.parentNodeId, node.position - 1)
                    }
                  >
                    ↑
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    aria-label={`${t('moveDown')}: ${node.title}`}
                    disabled={busy || index === siblings.length - 1}
                    onClick={() =>
                      void moveNode(node, node.parentNodeId, node.position + 1)
                    }
                  >
                    ↓
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    aria-label={`${t('indent')}: ${node.title}`}
                    disabled={busy || !previousSibling}
                    onClick={() => {
                      if (previousSibling) {
                        const childCount = nodes.filter(
                          (item) => item.parentNodeId === previousSibling.id,
                        ).length;
                        void moveNode(node, previousSibling.id, childCount);
                      }
                    }}
                  >
                    →
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    aria-label={`${t('outdent')}: ${node.title}`}
                    disabled={busy || !parent}
                    onClick={() => {
                      if (parent)
                        void moveNode(node, grandparents, parentIndex + 1);
                    }}
                  >
                    ←
                  </Button>
                  {node.isLink && (
                    <Button
                      type="button"
                      variant="secondary"
                      aria-label={`${t('removeFromTree')}: ${node.title}`}
                      disabled={busy}
                      onClick={() => removeNode(node)}
                    >
                      {t('removeFromTree')}
                    </Button>
                  )}
                </div>
              </div>
              {renderChildren(node.id)}
            </li>
          );
        })}
      </ul>
    );
  };

  return (
    <main className="mn-kit-theme page-tree-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={() => onNavigate(null)}>
          {t('backToAdmin')}
        </Button>
      </header>
      <section className="page-tree-content">
        <div className="content-heading">
          <div>
            <h1>{t('tree')}</h1>
            <p>{t('treeDescription')}</p>
          </div>
          <Button
            variant="secondary"
            onClick={() => onNavigate({ type: 'content-list' })}
          >
            {t('pages')}
          </Button>
        </div>
        {error && <p role="alert">{t(failureKey(error))}</p>}
        {notice && <p role="status">{t('treeSaved')}</p>}
        {loading ? (
          <p role="status">{t('loading')}</p>
        ) : nodes.length ? (
          <Card className="page-tree-card">{renderChildren(null)}</Card>
        ) : (
          <Card className="page-tree-card">
            <p>{t('treeEmpty')}</p>
          </Card>
        )}
        {!loading && (
          <div className="page-tree-forms">
            <Card className="page-tree-form">
              <h2>{t('createLink')}</h2>
              <p>{t('linkedPage')}</p>
              <label className="content-field">
                <span>{t('linkTarget')}</span>
                <select
                  value={linkTarget}
                  onChange={(event) => setLinkTarget(event.target.value)}
                >
                  {pages.map((page) => (
                    <option key={page.id} value={page.id}>
                      {page.title}
                    </option>
                  ))}
                </select>
              </label>
              <ParentSelect
                nodes={nodes}
                value={linkParent}
                t={t}
                onChange={setLinkParent}
              />
              <Button
                disabled={busy || !linkTarget || !linkParent}
                onClick={createSelectedLink}
              >
                {t('createLink')}
              </Button>
            </Card>
          </div>
        )}
      </section>
    </main>
  );
}

function ParentSelect({
  nodes,
  value,
  t,
  onChange,
}: {
  nodes: AdminTreeNode[];
  value: string;
  t: Props['translate'];
  onChange: (value: string) => void;
}) {
  return (
    <label className="content-field">
      <span>{t('placement')}</span>
      <select value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">{t('choosePlacement')}</option>
        <option value="root">{t('root')}</option>
        {nodes.map((node) => (
          <option key={node.id} value={node.id}>
            {`${node.title} · /${node.slug}${node.isLink ? ` · ${t('linkNode')}` : ''}`}
          </option>
        ))}
      </select>
    </label>
  );
}

function mapContentFailure(error: contentApi.ContentFailure): TreeFailure {
  return error === 'throttled' ? 'unavailable' : error;
}
