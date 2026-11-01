import { useCallback, useEffect, useRef, useState } from 'react';
import * as api from './authApi';
import type { AuthFailure } from './authApi';

type Phase = 'loading' | 'setup' | 'login' | 'authenticated' | 'error';
type Notice =
  AuthFailure | 'passwordMismatch' | 'setupComplete' | 'sessionExpired';

interface AuthState {
  phase: Phase;
  userName?: string;
  notice?: Notice;
  busy: boolean;
}

function navigate(phase: Phase) {
  if (
    phase === 'authenticated' &&
    (window.location.pathname.startsWith('/admin/settings/') ||
      window.location.pathname.startsWith('/admin/pages') ||
      window.location.pathname.startsWith('/admin/tree') ||
      window.location.pathname.startsWith('/admin/contexts'))
  ) {
    return;
  }
  const path =
    phase === 'setup'
      ? '/admin/setup'
      : phase === 'login'
        ? '/admin/login'
        : '/admin/';
  if (window.location.pathname !== path)
    window.history.replaceState(null, '', path);
}

function failure(error: unknown): AuthFailure {
  return error instanceof api.AuthError ? error.reason : 'unavailable';
}

export function useAdminAuth() {
  const [state, setState] = useState<AuthState>({
    phase: 'loading',
    busy: false,
  });
  const revision = useRef(0);

  const load = useCallback(async () => {
    const requestId = ++revision.current;
    try {
      const initialized = await api.getSetupStatus();
      const userName = initialized ? await api.getSession() : null;
      if (requestId !== revision.current) return;
      const phase = !initialized
        ? 'setup'
        : userName
          ? 'authenticated'
          : 'login';
      navigate(phase);
      setState({ phase, userName: userName ?? undefined, busy: false });
    } catch (error) {
      if (requestId !== revision.current) return;
      setState({ phase: 'error', notice: failure(error), busy: false });
    }
  }, []);

  const refresh = useCallback(() => {
    setState({ phase: 'loading', busy: false });
    void load();
  }, [load]);

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

  const submit = async (
    userName: string,
    password: string,
    confirmation: string,
  ) => {
    if (state.busy || (state.phase !== 'setup' && state.phase !== 'login'))
      return false;
    if (state.phase === 'setup' && password !== confirmation) {
      setState((current) => ({ ...current, notice: 'passwordMismatch' }));
      return false;
    }
    const phase = state.phase;
    setState((current) => ({ ...current, busy: true, notice: undefined }));
    try {
      if (phase === 'setup') {
        await api.initialize(userName, password);
        navigate('login');
        setState({ phase: 'login', notice: 'setupComplete', busy: false });
      } else {
        const authenticatedUser = await api.login(userName, password);
        navigate('authenticated');
        setState({
          phase: 'authenticated',
          userName: authenticatedUser,
          busy: false,
        });
      }
      return true;
    } catch (error) {
      const reason = failure(error);
      if (phase === 'setup' && reason === 'conflict') {
        navigate('login');
        setState({ phase: 'login', notice: 'conflict', busy: false });
        return true;
      } else {
        setState((current) => ({ ...current, busy: false, notice: reason }));
        return false;
      }
    }
  };

  const signOut = async () => {
    if (state.busy) return;
    setState((current) => ({ ...current, busy: true, notice: undefined }));
    try {
      await api.logout();
      navigate('login');
      setState({ phase: 'login', busy: false });
    } catch (error) {
      const reason = failure(error);
      if (reason === 'invalid') {
        navigate('login');
        setState({ phase: 'login', notice: 'sessionExpired', busy: false });
      } else {
        setState((current) => ({ ...current, busy: false, notice: reason }));
      }
    }
  };

  return { state, refresh, submit, signOut };
}
