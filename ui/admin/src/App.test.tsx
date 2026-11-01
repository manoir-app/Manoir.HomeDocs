import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';

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
});
