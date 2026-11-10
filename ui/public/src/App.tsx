import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent, MouseEvent } from 'react';
import ReactMarkdown from 'react-markdown';
import type { PublicInvocation, PublicNavigationItem } from './publicApi';
import { SearchBox } from './SearchBox';
import { useAppearance } from './appearance';
import { usePublicManual } from './usePublicManual';
import './app.css';

interface PublicRoute {
  invocation: PublicInvocation;
  slug: string | null;
}

function readRoute(): PublicRoute {
  const search = new URLSearchParams(window.location.search);
  const path = window.location.pathname.replace(/^\/+|\/+$/g, '');
  let slug: string | null;
  try {
    slug = path ? decodeURIComponent(path) : null;
  } catch {
    slug = path;
  }
  return {
    invocation: {
      context: search.get('context'),
      profile: search.get('profile'),
      device: search.get('device'),
    },
    slug,
  };
}

function safeLocalUrl(url: string): string {
  if (
    url.startsWith('//') ||
    url.includes('\\') ||
    [...url].some((character) => character.charCodeAt(0) <= 0x1f) ||
    /^[a-z][a-z\d+.-]*:/i.test(url)
  ) {
    return '';
  }
  return url;
}

function preserveInvocation(url: string, invocation: PublicInvocation): string {
  const resolved = new URL(url, window.location.origin);
  if (resolved.origin !== window.location.origin) return '';
  const values: Array<[string, string | null]> = [
    ['context', invocation.context],
    ['profile', invocation.profile],
    ['device', invocation.device],
  ];
  for (const [key, value] of values) {
    if (value) resolved.searchParams.set(key, value);
    else resolved.searchParams.delete(key);
  }
  return `${resolved.pathname}${resolved.search}${resolved.hash}`;
}

function profileLabel(profile: string | null): string {
  if (profile === 'tablet-wall') return 'Tablette murale';
  if (profile === 'tablet-dock') return 'Tablette posée';
  if (profile === 'tv') return 'Télévision';
  if (profile === 'mobile') return 'Mobile';
  if (profile === 'desktop') return 'Ordinateur';
  return 'Profil invalide';
}

export function App() {
  const [route, setRoute] = useState<PublicRoute>(readRoute);
  const [pin, setPin] = useState('');
  const t = useMemo(
    () => ({
      title: 'Manuel de la maison',
      home: 'Accueil du contexte',
      quickLinks: 'Accès rapides',
      navigation: 'Dans ce contexte',
      loading: 'Chargement du manuel…',
      unavailable: 'Le manuel est temporairement indisponible.',
      notFound: 'Aucune page ne correspond à ce contexte et à ce profil.',
      pinTitle: 'Page protégée',
      pinPrompt: 'Saisis le PIN à six chiffres pour continuer.',
      pinLabel: 'PIN',
      pinSubmit: 'Continuer',
      invalidPin: 'Ce PIN est incorrect. Réessaie.',
      tooManyPins: 'Trop de tentatives. Réessaie dans une minute.',
      pinUnavailable: 'La vérification du PIN est indisponible.',
      returnHome: 'Retour à la racine du contexte',
      lock: 'Protégée',
      context: 'Contexte',
    }),
    [],
  );

  const navigate = useCallback(
    (slug: string | null, replace = false) => {
      const search = new URLSearchParams();
      if (route.invocation.context)
        search.set('context', route.invocation.context);
      if (route.invocation.profile)
        search.set('profile', route.invocation.profile);
      if (route.invocation.device)
        search.set('device', route.invocation.device);
      const path = slug ? `/${encodeURIComponent(slug)}` : '/';
      const url = `${path}?${search.toString()}`;
      if (replace) window.history.replaceState(null, '', url);
      else window.history.pushState(null, '', url);
      setRoute(readRoute());
    },
    [route.invocation],
  );
  const replaceSlug = useCallback(
    (slug: string) => navigate(slug, true),
    [navigate],
  );
  const manual = usePublicManual(route.invocation, route.slug, replaceSlug);
  useAppearance(route.invocation);

  useEffect(() => {
    document.documentElement.lang = 'fr';
    const updateRoute = () => {
      setPin('');
      setRoute(readRoute());
    };
    window.addEventListener('popstate', updateRoute);
    return () => window.removeEventListener('popstate', updateRoute);
  }, []);

  const submitPin = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (await manual.unlock(pin)) setPin('');
  };

  if (manual.state.loading) {
    return (
      <main className="public-status" role="status">
        {t.loading}
      </main>
    );
  }

  if (manual.state.error && !manual.state.pinRequired) {
    const message =
      manual.state.error === 'invalidInvocation' ||
      manual.state.error === 'notFound'
        ? t.notFound
        : manual.state.error === 'rateLimited'
          ? t.tooManyPins
          : t.unavailable;
    return (
      <main className="public-status" role="alert">
        {message}
      </main>
    );
  }

  if (manual.state.pinRequired) {
    const pinError =
      manual.state.error === 'invalidPin'
        ? t.invalidPin
        : manual.state.error === 'rateLimited'
          ? t.tooManyPins
          : manual.state.error
            ? t.pinUnavailable
            : undefined;
    return (
      <main
        className={`public-shell profile-${route.invocation.profile ?? 'invalid'}`}
      >
        <header className="public-header">
          {manual.state.navigation ? (
            <a
              href={makeHref(null, route.invocation)}
              onClick={(event) => handleNavigation(event, null, navigate)}
            >
              {t.title}
            </a>
          ) : (
            <span>{t.title}</span>
          )}
          <span>{route.invocation.context}</span>
        </header>
        <section className="pin-gate" aria-labelledby="pin-title">
          <p className="public-eyebrow">{t.lock}</p>
          <h1 id="pin-title">{manual.state.protectedTitle ?? t.pinTitle}</h1>
          <p>{t.pinPrompt}</p>
          <form onSubmit={(event) => void submitPin(event)}>
            <label htmlFor="public-pin">{t.pinLabel}</label>
            <input
              id="public-pin"
              type="password"
              inputMode="numeric"
              pattern="[0-9]{6}"
              minLength={6}
              maxLength={6}
              autoComplete="one-time-code"
              required
              value={pin}
              onChange={(event) => setPin(event.target.value)}
            />
            {pinError && <p role="alert">{pinError}</p>}
            <button type="submit" disabled={manual.state.pinBusy}>
              {t.pinSubmit}
            </button>
          </form>
          {manual.state.navigation && (
            <a
              href={makeHref(null, route.invocation)}
              onClick={(event) => handleNavigation(event, null, navigate)}
            >
              {t.returnHome}
            </a>
          )}
        </section>
      </main>
    );
  }

  const navigation = manual.state.navigation;
  const page = manual.state.page;
  if (!page || (!navigation && !route.slug)) {
    return (
      <main className="public-status" role="alert">
        {t.notFound}
      </main>
    );
  }
  const quickLinks = navigation?.children.filter((item) => item.isLink) ?? [];
  const pageItems = navigation?.children.filter((item) => !item.isLink) ?? [];

  return (
    <main
      className={`public-shell profile-${navigation?.profile ?? route.invocation.profile ?? 'desktop'}`}
    >
      <header className="public-header">
        {navigation ? (
          <a
            href={makeHref(null, route.invocation)}
            onClick={(event) => handleNavigation(event, null, navigate)}
          >
            {t.title}
          </a>
        ) : (
          <span>{t.title}</span>
        )}
        <div className="public-invocation">
          <span>
            {t.context}: {navigation?.contextName ?? route.invocation.context}
          </span>
          <span>
            {profileLabel(navigation?.profile ?? route.invocation.profile)}
          </span>
          {(navigation?.device ?? route.invocation.device) && (
            <span>{navigation?.device ?? route.invocation.device}</span>
          )}
        </div>
      </header>
      <div className="public-layout">
        <aside className="public-navigation">
          <SearchBox
            invocation={route.invocation}
            hrefFor={(slug) => makeHref(slug, route.invocation)}
            onOpen={(event, slug) => handleNavigation(event, slug, navigate)}
          />
          <nav aria-label={t.navigation}>
            <h2>{t.navigation}</h2>
            {navigation ? (
              <NavigationList
                items={pageItems}
                invocation={route.invocation}
                currentSlug={page.slug}
                navigate={navigate}
              />
            ) : (
              <p>Aucune racine de navigation n’est définie pour ce contexte.</p>
            )}
          </nav>
          {quickLinks.length > 0 && (
            <nav className="quick-links" aria-label={t.quickLinks}>
              <h2>{t.quickLinks}</h2>
              <NavigationList
                items={quickLinks}
                invocation={route.invocation}
                currentSlug={page.slug}
                navigate={navigate}
              />
            </nav>
          )}
        </aside>
        <article className="public-page">
          {navigation && (
            <a
              className="home-link"
              href={makeHref(null, route.invocation)}
              onClick={(event) => handleNavigation(event, null, navigate)}
            >
              {t.home}
            </a>
          )}
          {page.requiresAuthentication && (
            <p className="protected-label">{t.lock}</p>
          )}
          <h1>{page.title}</h1>
          <div className="markdown-content">
            <ReactMarkdown
              skipHtml
              urlTransform={safeLocalUrl}
              components={{
                img: ({ src, alt }) =>
                  src ? (
                    <img
                      src={preserveInvocation(src, route.invocation)}
                      alt={alt ?? ''}
                      loading="lazy"
                    />
                  ) : null,
                a: ({ href, children }) => {
                  if (!href) return <>{children}</>;
                  return (
                    <a href={preserveInvocation(href, route.invocation)}>
                      {children}
                    </a>
                  );
                },
              }}
            >
              {page.markdown}
            </ReactMarkdown>
          </div>
        </article>
      </div>
    </main>
  );
}

function NavigationList({
  items,
  invocation,
  currentSlug,
  navigate,
}: {
  items: PublicNavigationItem[];
  invocation: PublicInvocation;
  currentSlug: string;
  navigate: (slug: string | null) => void;
}) {
  if (items.length === 0) return null;
  return (
    <ul>
      {items.map((item) => (
        <li key={item.nodeId}>
          <a
            href={makeHref(item.slug, invocation)}
            aria-current={item.slug === currentSlug ? 'page' : undefined}
            onClick={(event) => handleNavigation(event, item.slug, navigate)}
          >
            <span>{item.title}</span>
            {item.requiresAuthentication && (
              <span className="protected-label">Protégée</span>
            )}
          </a>
          {!item.requiresAuthentication && (
            <NavigationList
              items={item.children}
              invocation={invocation}
              currentSlug={currentSlug}
              navigate={navigate}
            />
          )}
        </li>
      ))}
    </ul>
  );
}

function makeHref(slug: string | null, invocation: PublicInvocation): string {
  const search = new URLSearchParams();
  if (invocation.context) search.set('context', invocation.context);
  if (invocation.profile) search.set('profile', invocation.profile);
  if (invocation.device) search.set('device', invocation.device);
  return `${slug ? `/${encodeURIComponent(slug)}` : '/'}?${search.toString()}`;
}

function handleNavigation(
  event: MouseEvent<HTMLAnchorElement>,
  slug: string | null,
  navigate: (slug: string | null) => void,
) {
  if (
    event.button !== 0 ||
    event.metaKey ||
    event.ctrlKey ||
    event.shiftKey ||
    event.altKey
  )
    return;
  event.preventDefault();
  navigate(slug);
}
