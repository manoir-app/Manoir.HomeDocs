import { useCallback, useEffect, useRef, useState } from 'react';
import * as api from './adminSettingsApi';
import type { AdminSettings } from './adminSettingsApi';
import type { Language } from './translations';

interface SettingsState {
  settings?: AdminSettings;
  busy: boolean;
  error?: api.SettingsFailure;
  notice?: 'saved';
}

function failure(error: unknown): api.SettingsFailure {
  return error instanceof api.SettingsError ? error.reason : 'unavailable';
}

export function useAdminSettings(enabled: boolean) {
  const [state, setState] = useState<SettingsState>({ busy: false });
  const revision = useRef(0);

  const load = useCallback(async () => {
    const requestId = ++revision.current;
    try {
      const settings = await api.getSettings();
      if (requestId === revision.current) setState({ settings, busy: false });
    } catch (error) {
      if (requestId === revision.current)
        setState({ busy: false, error: failure(error) });
    }
  }, []);

  useEffect(() => {
    const activeRevision = revision;
    let active = true;
    if (enabled) {
      queueMicrotask(() => {
        if (active) void load();
      });
    } else {
      revision.current++;
    }
    return () => {
      active = false;
      activeRevision.current++;
    };
  }, [enabled, load]);

  const run = useCallback(
    async (
      operation: () => Promise<void>,
      update: (settings: AdminSettings) => AdminSettings,
    ) => {
      const current = state.settings;
      if (!current || state.busy) return false;
      setState({ settings: current, busy: true });
      try {
        await operation();
        setState({ settings: update(current), busy: false, notice: 'saved' });
        return true;
      } catch (error) {
        const reason = failure(error);
        if (reason === 'sessionExpired') {
          setState({ busy: false, error: reason });
        } else {
          setState({ settings: current, busy: false, error: reason });
        }
        return false;
      }
    },
    [state.busy, state.settings],
  );

  const changeLanguage = (language: Language) =>
    run(
      () => api.updateLanguage(language),
      (settings) => ({ ...settings, language }),
    );

  const changeAccount = (
    currentPassword: string,
    userName: string,
    newPassword: string | null,
  ) =>
    run(
      () => api.updateAccount(currentPassword, userName, newPassword),
      (settings) => ({ ...settings, userName: userName.trim() }),
    );

  const changePin = (currentPassword: string, pin: string | null) =>
    run(
      () => api.updatePin(currentPassword, pin),
      (settings) => ({
        ...settings,
        pinConfigured: pin !== null,
      }),
    );

  const clearNotice = () =>
    setState((current) => ({
      ...current,
      error: undefined,
      notice: undefined,
    }));

  return {
    state,
    changeLanguage,
    changeAccount,
    changePin,
    clearNotice,
    reload: load,
  };
}
