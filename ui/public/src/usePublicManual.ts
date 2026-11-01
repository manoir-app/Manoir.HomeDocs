import { useCallback, useEffect, useRef, useState } from 'react';
import * as api from './publicApi';
import type {
  PublicInvocation,
  PublicNavigation,
  PublicNavigationItem,
  PublicPage,
} from './publicApi';

interface ManualState {
  navigation?: PublicNavigation;
  page?: PublicPage;
  protectedTitle?: string;
  loading: boolean;
  pinRequired: boolean;
  pinBusy: boolean;
  error?: api.PublicFailure;
}

function findPage(
  navigation: PublicNavigation | undefined,
  slug: string,
): PublicNavigationItem | undefined {
  if (!navigation) return undefined;
  const search = (
    item: PublicNavigationItem,
  ): PublicNavigationItem | undefined => {
    if (item.slug === slug) return item;
    for (const child of item.children) {
      const found = search(child);
      if (found) return found;
    }
    return undefined;
  };
  return search(navigation.root);
}

export function usePublicManual(
  invocation: PublicInvocation,
  slug: string | null,
  replaceSlug: (slug: string) => void,
) {
  const [state, setState] = useState<ManualState>({
    loading: true,
    pinRequired: false,
    pinBusy: false,
  });
  const revision = useRef(0);

  const load = useCallback(async () => {
    const requestId = ++revision.current;
    setState((current) => ({
      ...current,
      loading: true,
      error: undefined,
      pinRequired: false,
      pinBusy: false,
    }));
    try {
      let navigation: PublicNavigation | undefined;
      try {
        navigation = await api.getNavigation(invocation);
      } catch (reason) {
        if (!(
          slug &&
          reason instanceof api.PublicApiError &&
          reason.reason === 'notFound'
        ))
          throw reason;
      }

      const requestedSlug = slug ?? navigation?.root.slug;
      if (!requestedSlug) throw new api.PublicApiError('notFound');
      try {
        const page = await api.getPage(requestedSlug, invocation);
        if (requestId !== revision.current) return;
        if (page.slug !== requestedSlug) replaceSlug(page.slug);
        setState({
          navigation,
          page,
          loading: false,
          pinRequired: false,
          pinBusy: false,
        });
      } catch (reason) {
        if (requestId !== revision.current) return;
        if (
          reason instanceof api.PublicApiError &&
          reason.reason === 'pinRequired'
        ) {
          setState({
            navigation,
            protectedTitle: findPage(navigation, requestedSlug)?.title,
            loading: false,
            pinRequired: true,
            pinBusy: false,
          });
          return;
        }
        throw reason;
      }
    } catch (reason) {
      if (requestId !== revision.current) return;
      setState({
        loading: false,
        pinRequired: false,
        pinBusy: false,
        error:
          reason instanceof api.PublicApiError ? reason.reason : 'unavailable',
      });
    }
  }, [invocation, replaceSlug, slug]);

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

  const lock = useCallback((error?: api.PublicFailure) => {
    setState((current) => ({
      ...current,
      page: undefined,
      protectedTitle: current.protectedTitle ?? current.page?.title,
      loading: false,
      pinRequired: true,
      pinBusy: false,
      error,
    }));
  }, []);

  useEffect(() => {
    if (!state.page?.requiresAuthentication) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    let lastRenewal = Date.now();
    const scheduleLock = () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        if (active) lock();
      }, api.PublicPinIdleTimeoutMs);
    };
    const markActivity = () => {
      scheduleLock();
      if (Date.now() - lastRenewal < 5000) return;
      lastRenewal = Date.now();
      void api
        .refreshProtectedAccess(invocation)
        .then((authorized) => {
          if (active && !authorized) lock();
        })
        .catch((reason) => {
          if (active) {
            lock(
              reason instanceof api.PublicApiError
                ? reason.reason
                : 'unavailable',
            );
          }
        });
    };
    scheduleLock();
    window.addEventListener('pointerdown', markActivity);
    window.addEventListener('keydown', markActivity);
    window.addEventListener('touchstart', markActivity, { passive: true });
    window.addEventListener('scroll', markActivity, { passive: true });
    window.addEventListener('focus', markActivity);
    return () => {
      active = false;
      clearTimeout(timer);
      window.removeEventListener('pointerdown', markActivity);
      window.removeEventListener('keydown', markActivity);
      window.removeEventListener('touchstart', markActivity);
      window.removeEventListener('scroll', markActivity);
      window.removeEventListener('focus', markActivity);
    };
  }, [invocation, lock, state.page]);

  const unlock = useCallback(
    async (pin: string) => {
      if (!invocation.context || !api.isDisplayProfile(invocation.profile))
        return false;
      setState((current) => ({ ...current, pinBusy: true, error: undefined }));
      try {
        await api.verifyPin(invocation, pin);
        await load();
        return true;
      } catch (reason) {
        setState((current) => ({
          ...current,
          pinBusy: false,
          error:
            reason instanceof api.PublicApiError
              ? reason.reason
              : 'unavailable',
        }));
        return false;
      }
    },
    [invocation, load],
  );

  const returnToRoot = useCallback(async () => {
    try {
      await api.logoutProtectedAccess();
    } catch {
      setState((current) => ({ ...current, error: 'unavailable' }));
    }
  }, []);

  return { state, reload: load, unlock, lock, returnToRoot };
}
