import { useEffect } from 'react';
import type { RefObject } from 'react';

const focusableSelector =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled])';
const backKeys = new Set(['Escape', 'BrowserBack', 'GoBack']);

function isTextEntry(element: Element | null): boolean {
  if (!(element instanceof HTMLElement)) return false;
  if (element.isContentEditable) return true;
  if (
    element instanceof HTMLTextAreaElement ||
    element instanceof HTMLSelectElement
  )
    return true;
  return (
    element instanceof HTMLInputElement &&
    !['button', 'submit', 'checkbox', 'radio'].includes(element.type)
  );
}

function focusables(root: Element): HTMLElement[] {
  return Array.from(
    root.querySelectorAll<HTMLElement>(focusableSelector),
  ).filter((element) => !element.closest('[hidden], [inert]'));
}

function inViewport(element: HTMLElement): boolean {
  const rect = element.getBoundingClientRect();
  return rect.bottom >= 0 && rect.top <= window.innerHeight;
}

/**
 * Arrow keys, Enter and Back for a keyboard-like remote. Up and Down move through the
 * navigation; in the page they only jump to a link already on screen, so the arrows keep
 * scrolling the text. Left and Right switch between the navigation and the page.
 * Enter keeps its native meaning on links and buttons.
 */
export function useKeyboardNavigation(
  shell: RefObject<HTMLElement | null>,
  onBack: () => void,
  enabled: boolean,
): void {
  useEffect(() => {
    if (!enabled) return;
    const handle = (event: KeyboardEvent) => {
      const root = shell.current;
      if (!root || event.defaultPrevented) return;
      if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey)
        return;
      const target = event.target instanceof Element ? event.target : null;
      if (!target || !root.contains(target)) return;
      const typing = isTextEntry(target);
      const search =
        target instanceof HTMLInputElement && target.type === 'search';

      if (backKeys.has(event.key) || (event.key === 'Backspace' && !typing)) {
        // A field with text keeps Escape for clearing itself.
        if (typing && (target as HTMLInputElement).value !== '') return;
        event.preventDefault();
        onBack();
        return;
      }

      const navigation = root.querySelector<HTMLElement>('.public-navigation');
      const content = root.querySelector<HTMLElement>('.public-page');
      if (!navigation || !content) return;
      const inNavigation = navigation.contains(target);

      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        if (typing && !search) return;
        const region = inNavigation ? navigation : content;
        const items = focusables(region);
        const index = items.indexOf(target as HTMLElement);
        if (index < 0) return;
        const next = items[index + (event.key === 'ArrowDown' ? 1 : -1)];
        if (!next) return;
        if (!inNavigation && !inViewport(next)) return;
        event.preventDefault();
        next.focus();
        next.scrollIntoView?.({ block: 'nearest' });
        return;
      }

      if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
        if (typing) return;
        if (event.key === 'ArrowRight' && inNavigation) {
          const first = focusables(content)[0];
          event.preventDefault();
          (first ?? content).focus();
        } else if (event.key === 'ArrowLeft' && !inNavigation) {
          const items = focusables(navigation);
          const current = navigation.querySelector<HTMLElement>(
            '[aria-current="page"]',
          );
          const next = current ?? items[0];
          if (!next) return;
          event.preventDefault();
          next.focus();
        }
      }
    };
    document.addEventListener('keydown', handle);
    return () => document.removeEventListener('keydown', handle);
  }, [shell, onBack, enabled]);
}

export const IdleReturnDelayMs = 120_000;

/**
 * Calls onIdle once after a period without any key, pointer or touch activity.
 * Used by remote-controlled screens to come back to the root of the current context.
 */
export function useIdleReturn(
  enabled: boolean,
  onIdle: () => void,
  delayMs: number = IdleReturnDelayMs,
): void {
  useEffect(() => {
    if (!enabled) return;
    let timer = window.setTimeout(onIdle, delayMs);
    const reset = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(onIdle, delayMs);
    };
    const events = ['keydown', 'pointerdown', 'touchstart', 'wheel'] as const;
    for (const name of events)
      document.addEventListener(name, reset, { passive: true });
    return () => {
      window.clearTimeout(timer);
      for (const name of events) document.removeEventListener(name, reset);
    };
  }, [enabled, onIdle, delayMs]);
}
