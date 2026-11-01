import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { browserLanguage } from './translations';

const password = 'correct horse battery staple';

function respond(status: number, body?: object): Response {
  return new Response(body ? JSON.stringify(body) : null, {
    status,
    headers: body ? { 'Content-Type': 'application/json' } : {},
  });
}

describe('administration', () => {
  beforeEach(() => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['fr-FR']);
    window.history.replaceState(null, '', '/admin/');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('uses the first supported browser language and falls back to French', () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE', 'en-GB']);
    expect(browserLanguage()).toBe('en');
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE', 'es-ES']);
    expect(browserLanguage()).toBe('fr');
  });

  it('prefers the saved language over the browser after signing in', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['fr-FR']);
    window.history.replaceState(null, '', '/admin/settings/preferences');
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input.endsWith('/status'))
          return respond(200, { isInitialized: true });
        if (input.endsWith('/session'))
          return respond(200, { userName: 'admin' });
        if (input.endsWith('/settings/'))
          return respond(200, {
            userName: 'admin',
            pinConfigured: false,
            language: 'en',
          });
        throw new Error(`Unexpected request: ${input}`);
      }),
    );
    render(<App />);

    await screen.findByRole('heading', { name: 'UI preferences' });
    await waitFor(() => expect(document.documentElement.lang).toBe('en'));
    expect(screen.getByRole('button', { name: 'EN' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('creates an administrator, signs in and signs out', async () => {
    let initialized = false;
    let signedIn = false;
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input.endsWith('/status'))
        return respond(200, { isInitialized: initialized });
      if (input.endsWith('/antiforgery'))
        return respond(200, { token: 'test-token' });
      if (input.endsWith('/initialize')) {
        expect(init?.headers).toHaveProperty('X-CSRF-TOKEN', 'test-token');
        initialized = true;
        return respond(201);
      }
      if (input.endsWith('/login')) {
        signedIn = true;
        return respond(204);
      }
      if (input.endsWith('/session')) {
        return signedIn ? respond(200, { userName: 'admin' }) : respond(401);
      }
      if (input.endsWith('/logout')) {
        signedIn = false;
        return respond(204);
      }
      throw new Error(`Unexpected request: ${input}`);
    });
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    await screen.findByRole('heading', {
      name: 'Créer le compte administrateur',
    });
    fireEvent.change(screen.getByLabelText(/^Nom d’utilisateur/), {
      target: { value: 'admin' },
    });
    fireEvent.change(screen.getByLabelText(/^Mot de passe/), {
      target: { value: password },
    });
    fireEvent.change(screen.getByLabelText(/^Confirmer le mot de passe/), {
      target: { value: password },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Créer le compte' }));

    await screen.findByRole('heading', { name: 'Connexion' });
    expect(screen.getByRole('status')).toHaveTextContent('Compte créé');
    fireEvent.change(screen.getByLabelText(/^Mot de passe/), {
      target: { value: password },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Se connecter' }));

    await screen.findByRole('heading', { name: 'Administration' });
    expect(screen.getByText('Compte connecté : admin')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Se déconnecter' }));
    await screen.findByRole('heading', { name: 'Connexion' });
    expect(window.location.pathname).toBe('/admin/login');
  });

  it('rejects mismatched confirmation without sending credentials', async () => {
    const fetchMock = vi.fn(async () => respond(200, { isInitialized: false }));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    await screen.findByRole('heading', {
      name: 'Créer le compte administrateur',
    });
    fireEvent.change(screen.getByLabelText(/^Nom d’utilisateur/), {
      target: { value: 'admin' },
    });
    fireEvent.change(screen.getByLabelText(/^Mot de passe/), {
      target: { value: password },
    });
    fireEvent.change(screen.getByLabelText(/^Confirmer le mot de passe/), {
      target: { value: `${password}x` },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Créer le compte' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Les mots de passe ne correspondent pas.',
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText(/^Mot de passe/)).toHaveValue(password);
  });

  it('shows login failures and allows switching to English', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input.endsWith('/status'))
          return respond(200, { isInitialized: true });
        if (input.endsWith('/antiforgery'))
          return respond(200, { token: 'test-token' });
        return respond(401);
      }),
    );
    render(<App />);

    await screen.findByRole('heading', { name: 'Connexion' });
    fireEvent.click(screen.getByRole('button', { name: 'EN' }));
    fireEvent.change(screen.getByLabelText(/^Username/), {
      target: { value: 'admin' },
    });
    fireEvent.change(screen.getByLabelText(/^Password/), {
      target: { value: password },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Incorrect username or password.',
    );
    expect(document.documentElement.lang).toBe('en');
  });

  it('offers retry when the API is unavailable', async () => {
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue(respond(200, { isInitialized: false }));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Le service est indisponible',
    );
    fireEvent.click(screen.getByRole('button', { name: 'Réessayer' }));
    await waitFor(() =>
      expect(
        screen.getByRole('heading', { name: 'Créer le compte administrateur' }),
      ).toBeVisible(),
    );
  });

  it('handles a setup conflict by switching to sign in', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input.endsWith('/status'))
          return respond(200, { isInitialized: false });
        if (input.endsWith('/antiforgery'))
          return respond(200, { token: 'test-token' });
        return respond(409);
      }),
    );
    render(<App />);

    await screen.findByRole('heading', {
      name: 'Créer le compte administrateur',
    });
    fireEvent.change(screen.getByLabelText(/^Nom d’utilisateur/), {
      target: { value: 'admin' },
    });
    fireEvent.change(screen.getByLabelText(/^Mot de passe/), {
      target: { value: password },
    });
    fireEvent.change(screen.getByLabelText(/^Confirmer le mot de passe/), {
      target: { value: password },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Créer le compte' }));

    await screen.findByRole('heading', { name: 'Connexion' });
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Un compte administrateur existe déjà.',
    );
    expect(screen.getByLabelText(/^Mot de passe/)).toHaveValue('');
  });

  it('reports throttling without discarding the entered password', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input.endsWith('/status'))
          return respond(200, { isInitialized: true });
        if (input.endsWith('/antiforgery'))
          return respond(200, { token: 'test-token' });
        return respond(input.endsWith('/session') ? 401 : 429);
      }),
    );
    render(<App />);

    await screen.findByRole('heading', { name: 'Connexion' });
    fireEvent.change(screen.getByLabelText(/^Nom d’utilisateur/), {
      target: { value: 'admin' },
    });
    fireEvent.change(screen.getByLabelText(/^Mot de passe/), {
      target: { value: password },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Se connecter' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Trop de tentatives.',
    );
    expect(screen.getByLabelText(/^Mot de passe/)).toHaveValue(password);
  });

  it('returns to sign in when the session expires before logout', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input.endsWith('/status'))
          return respond(200, { isInitialized: true });
        if (input.endsWith('/session'))
          return respond(200, { userName: 'admin' });
        if (input.endsWith('/settings/'))
          return respond(200, {
            userName: 'admin',
            pinConfigured: false,
            language: 'fr',
          });
        if (input.endsWith('/antiforgery'))
          return respond(200, { token: 'test-token' });
        return respond(401);
      }),
    );
    render(<App />);

    await screen.findByRole('heading', { name: 'Administration' });
    fireEvent.click(screen.getByRole('button', { name: 'Se déconnecter' }));
    await screen.findByRole('heading', { name: 'Connexion' });
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Votre session a expiré.',
    );
    expect(window.location.pathname).toBe('/admin/login');
  });

  it('saves the interface language and changes account and PIN from settings', async () => {
    const settings = {
      userName: 'admin',
      pinConfigured: false,
      language: 'fr',
    };
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input.endsWith('/status'))
        return respond(200, { isInitialized: true });
      if (input.endsWith('/session'))
        return respond(200, { userName: settings.userName });
      if (input.endsWith('/settings/')) return respond(200, settings);
      if (input.endsWith('/antiforgery'))
        return respond(200, { token: 'settings-token' });
      if (input.endsWith('/settings/language')) {
        settings.language = JSON.parse(String(init?.body)).language;
        return respond(204);
      }
      if (input.endsWith('/settings/account')) {
        const body = JSON.parse(String(init?.body));
        expect(body.currentPassword).toBe(password);
        expect(body.newPassword).toBe('new-secure-password');
        settings.userName = body.userName;
        return respond(204);
      }
      if (input.endsWith('/settings/pin')) {
        const body = JSON.parse(String(init?.body));
        expect(body.currentPassword).toBe(password);
        expect(body.pin).toBe('012345');
        settings.pinConfigured = true;
        return respond(204);
      }
      throw new Error(`Unexpected request: ${input}`);
    });
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<App />);

    await screen.findByRole('heading', { name: 'Administration' });
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/admin/settings/',
        expect.objectContaining({ credentials: 'same-origin' }),
      ),
    );
    fireEvent.click(
      screen.getByRole('button', { name: 'Ouvrir les paramètres' }),
    );
    await screen.findByRole('heading', { name: 'Préférences UI' });
    fireEvent.click(screen.getByRole('button', { name: 'EN' }));
    await waitFor(() => expect(document.documentElement.lang).toBe('en'));

    fireEvent.click(
      screen.getByRole('button', { name: 'Home Assistant integration' }),
    );
    expect(
      await screen.findByText('Configuration will be added in HD-16.'),
    ).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Other integrations' }));
    expect(
      await screen.findByText(
        'Other integrations will be added in a later step.',
      ),
    ).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Security' }));
    await screen.findByRole('heading', { name: 'Administrator account' });
    const field = (id: string) =>
      container.querySelector<HTMLInputElement>(`#${id}`)!;
    fireEvent.change(field('settings-username'), {
      target: { value: 'new-admin' },
    });
    fireEvent.change(field('settings-current-password'), {
      target: { value: password },
    });
    fireEvent.change(field('settings-new-password'), {
      target: { value: 'new-secure-password' },
    });
    fireEvent.change(field('settings-confirm-password'), {
      target: { value: 'new-secure-password' },
    });
    fireEvent.click(screen.getByRole('button', { name: /^Save$/ }));
    await screen.findByText('Changes saved.');
    await waitFor(() =>
      expect(field('settings-username')).toHaveValue('new-admin'),
    );

    fireEvent.change(field('settings-pin-current-password'), {
      target: { value: password },
    });
    fireEvent.change(field('settings-pin'), { target: { value: '012345' } });
    fireEvent.change(field('settings-confirm-pin'), {
      target: { value: '012345' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save PIN' }));
    await waitFor(() =>
      expect(screen.getByText('A PIN is configured.')).toBeVisible(),
    );
    expect(field('settings-pin')).toHaveValue('');
  });

  it('creates, previews, publishes and edits a Markdown page', async () => {
    let created = false;
    const page = {
      id: '8e533d54-19a2-4a14-8a01-d55f19bd2ccb',
      title: 'Cuisine',
      slug: 'cuisine',
      category: 'piece',
      contexts: ['invites'],
      requiresAuthentication: false,
      isPublished: false,
      markdown: '# Cuisine',
      revision: 1,
    };
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input.endsWith('/status'))
        return respond(200, { isInitialized: true });
      if (input.endsWith('/session'))
        return respond(200, { userName: 'admin' });
      if (input.endsWith('/settings/'))
        return respond(200, {
          userName: 'admin',
          pinConfigured: false,
          language: 'fr',
        });
      if (input.endsWith('/pages/') && (!init || init.method === undefined))
        return respond(200, created ? [page] : []);
      if (input.endsWith('/antiforgery'))
        return respond(200, { token: 'content-token' });
      if (input.endsWith('/pages/') && init?.method === 'POST') {
        created = true;
        Object.assign(page, JSON.parse(String(init.body)));
        return respond(201, { id: page.id });
      }
      if (
        input.endsWith(`/pages/${page.id}`) &&
        (!init || init.method === undefined)
      )
        return respond(200, page);
      if (input.endsWith(`/pages/${page.id}/publication`)) {
        page.isPublished = JSON.parse(String(init?.body)).isPublished;
        page.revision++;
        return respond(204);
      }
      if (input.endsWith(`/pages/${page.id}`) && init?.method === 'PUT') {
        Object.assign(page, JSON.parse(String(init.body)).page);
        page.revision++;
        return respond(204);
      }
      throw new Error(`Unexpected request: ${input} ${init?.method ?? 'GET'}`);
    });
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<App />);

    await screen.findByRole('heading', { name: 'Administration' });
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/admin/settings/',
        expect.objectContaining({ credentials: 'same-origin' }),
      ),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Gérer les contenus' }));
    await screen.findByText('Aucune page pour le moment.');
    fireEvent.click(screen.getByRole('button', { name: 'Créer une page' }));
    await screen.findByRole('heading', { name: 'Créer une page' });
    fireEvent.change(screen.getByLabelText(/^Titre/), {
      target: { value: 'Cuisine' },
    });
    fireEvent.change(screen.getByLabelText(/^Slug/), {
      target: { value: 'cuisine' },
    });
    fireEvent.change(screen.getByLabelText(/^Contextes/), {
      target: { value: 'invites' },
    });

    expect(
      screen.getByRole('textbox', { name: 'Éditeur Markdown' }),
    ).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Aperçu' }));
    await screen.findByRole('heading', { name: 'Aperçu' });
    expect(container.querySelector('.markdown-preview')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Contenu Markdown' }));
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));

    await screen.findByRole('heading', { name: 'Modifier la page' });
    fireEvent.click(screen.getByRole('button', { name: 'Publier' }));
    expect(await screen.findByText(/Page publiée/)).toBeVisible();
    expect(page.isPublished).toBe(true);

    fireEvent.change(screen.getByLabelText(/^Titre/), {
      target: { value: 'Cuisine mise à jour' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await screen.findByText('Page enregistrée.');
    expect(page.title).toBe('Cuisine mise à jour');
    expect(page.isPublished).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Pages' }));
    expect(await screen.findByText('Cuisine mise à jour')).toBeVisible();
    expect(screen.getByText('Publiée')).toBeVisible();
  });
});
