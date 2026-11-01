import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Button, Card, Field, TextField } from '@manoir-app/core-admin-ui-kit';
import * as contextApi from './adminContextApi';
import type { AdminUsageContext, ContextFailure } from './adminContextApi';
import type { AdminRoute } from './adminRoutes';
import * as treeApi from './adminTreeApi';
import type { AdminTreeNode } from './adminTreeApi';
import type { TranslationKey } from './translations';

interface Props {
  translate: (key: TranslationKey) => string;
  onNavigate: (route: AdminRoute) => void;
  onSessionExpired: () => void;
}

function failureKey(error: ContextFailure): TranslationKey {
  if (error === 'invalid') return 'contextInvalid';
  if (error === 'invalidRoot') return 'contextRootInvalid';
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'notFound') return 'contextUnavailable';
  if (error === 'conflict') return 'contextConflict';
  return 'contextUnavailable';
}

export function AdminContexts({
  translate: t,
  onNavigate,
  onSessionExpired,
}: Props) {
  const [contexts, setContexts] = useState<AdminUsageContext[]>([]);
  const [nodes, setNodes] = useState<AdminTreeNode[]>([]);
  const [currentId, setCurrentId] = useState<string>();
  const [id, setId] = useState('');
  const [name, setName] = useState('');
  const [rootNodeId, setRootNodeId] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ContextFailure>();
  const [notice, setNotice] = useState<'saved' | 'deleted'>();
  const requestRevision = useRef(0);

  const reload = useCallback(async () => {
    const requestId = ++requestRevision.current;
    setLoading(true);
    setError(undefined);
    try {
      const [contextList, treeNodes] = await Promise.all([
        contextApi.listContexts(),
        treeApi.listTree(),
      ]);
      if (requestId === requestRevision.current) {
        setContexts(contextList);
        setNodes(treeNodes);
      }
      return true;
    } catch (reason) {
      if (requestId === requestRevision.current) {
        const failure =
          reason instanceof contextApi.ContextError
            ? reason.reason
            : reason instanceof treeApi.TreeError
              ? reason.reason
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

  const reset = () => {
    setCurrentId(undefined);
    setId('');
    setName('');
    setRootNodeId('');
    setError(undefined);
    setNotice(undefined);
  };

  const edit = (context: AdminUsageContext) => {
    setCurrentId(context.id);
    setId(context.id);
    setName(context.name);
    setRootNodeId(context.rootNodeId ?? '');
    setError(undefined);
    setNotice(undefined);
  };

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      const context = {
        id: id.trim(),
        name: name.trim(),
        rootNodeId: rootNodeId || null,
      };
      if (currentId) await contextApi.updateContext(currentId, context);
      else await contextApi.createContext(context);
      if (await reload()) {
        setCurrentId(context.id);
        setId(context.id);
        setName(context.name);
        setRootNodeId(context.rootNodeId ?? '');
        setNotice('saved');
      }
    } catch (reason) {
      const failure =
        reason instanceof contextApi.ContextError
          ? reason.reason
          : 'unavailable';
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!currentId || !window.confirm(t('confirmDeleteContext'))) return;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      await contextApi.deleteContext(currentId);
      reset();
      if (await reload()) setNotice('deleted');
    } catch (reason) {
      const failure =
        reason instanceof contextApi.ContextError
          ? reason.reason
          : 'unavailable';
      setError(failure);
      if (failure === 'sessionExpired') onSessionExpired();
    } finally {
      setBusy(false);
    }
  };

  const rootLabel = (nodeId: string | null) => {
    if (!nodeId) return t('noRoot');
    const node = nodes.find((item) => item.id === nodeId);
    return node ? `${node.title} · /${node.slug}` : t('contextRootInvalid');
  };

  return (
    <main className="mn-kit-theme contexts-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={() => onNavigate(null)}>
          {t('backToAdmin')}
        </Button>
      </header>
      <section className="contexts-content">
        <div className="content-heading">
          <div>
            <h1>{t('usageContexts')}</h1>
            <p>{t('contextsDescription')}</p>
          </div>
          <Button
            variant="secondary"
            onClick={() => onNavigate({ type: 'tree' })}
          >
            {t('tree')}
          </Button>
        </div>
        {error && <p role="alert">{t(failureKey(error))}</p>}
        {notice && (
          <p role="status">
            {t(notice === 'deleted' ? 'contextDeleted' : 'contextSaved')}
          </p>
        )}
        <div className="contexts-layout">
          <Card className="contexts-list">
            <div className="contexts-list-header">
              <h2>{t('usageContexts')}</h2>
              <Button variant="secondary" disabled={busy} onClick={reset}>
                {t('createContext')}
              </Button>
            </div>
            {loading ? (
              <p role="status">{t('loading')}</p>
            ) : contexts.length === 0 ? (
              <p>{t('noContexts')}</p>
            ) : (
              <ul>
                {contexts.map((context) => (
                  <li key={context.id}>
                    <button
                      type="button"
                      aria-current={
                        currentId === context.id ? 'true' : undefined
                      }
                      onClick={() => edit(context)}
                    >
                      <strong>{context.name}</strong>
                      <small>
                        {context.id} · {rootLabel(context.rootNodeId)}
                      </small>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>
          <Card className="contexts-form-card">
            <h2>{t(currentId ? 'editContext' : 'createContext')}</h2>
            <form className="auth-form" onSubmit={save}>
              <Field htmlFor="context-id" label={t('contextId')} required>
                <TextField
                  id="context-id"
                  maxLength={64}
                  pattern="[a-z0-9]+(-[a-z0-9]+)*"
                  required
                  value={id}
                  onChange={(event) => setId(event.target.value)}
                />
              </Field>
              <Field htmlFor="context-name" label={t('contextName')} required>
                <TextField
                  id="context-name"
                  maxLength={128}
                  required
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
              </Field>
              <label className="content-field" htmlFor="context-root">
                <span>{t('contextRoot')}</span>
                <select
                  id="context-root"
                  value={rootNodeId}
                  onChange={(event) => setRootNodeId(event.target.value)}
                >
                  <option value="">{t('noRoot')}</option>
                  {nodes.map((node) => (
                    <option key={node.id} value={node.id}>
                      {`${node.title} · /${node.slug}${node.isLink ? ` · ${t('linkNode')}` : ''}`}
                    </option>
                  ))}
                </select>
              </label>
              <div className="contexts-actions">
                <Button type="submit" disabled={busy || loading}>
                  {t('save')}
                </Button>
                {currentId && (
                  <Button
                    type="button"
                    variant="secondary"
                    disabled={busy}
                    onClick={() => void remove()}
                  >
                    {t('deleteContext')}
                  </Button>
                )}
              </div>
            </form>
          </Card>
        </div>
      </section>
    </main>
  );
}
