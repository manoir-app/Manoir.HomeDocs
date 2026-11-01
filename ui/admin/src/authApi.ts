export type AuthFailure =
  'invalid' | 'invalidInput' | 'conflict' | 'throttled' | 'unavailable';

export class AuthError extends Error {
  constructor(public readonly reason: AuthFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin/auth/${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new AuthError('unavailable');
  }

  if (!response.ok) {
    if (response.status === 400) throw new AuthError('invalidInput');
    if (response.status === 401) throw new AuthError('invalid');
    if (response.status === 409) throw new AuthError('conflict');
    if (response.status === 429) throw new AuthError('throttled');
    throw new AuthError('unavailable');
  }
  return response;
}

async function token(): Promise<string> {
  const response = await request('antiforgery');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token) {
    throw new AuthError('unavailable');
  }
  return data.token;
}

export async function getSetupStatus(): Promise<boolean> {
  const response = await request('status');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.isInitialized !== 'boolean') {
    throw new AuthError('unavailable');
  }
  return data.isInitialized;
}

export async function getSession(): Promise<string | null> {
  try {
    const response = await request('session');
    const data: unknown = await response.json();
    if (
      !isObject(data) ||
      typeof data.userName !== 'string' ||
      !data.userName
    ) {
      throw new AuthError('unavailable');
    }
    return data.userName;
  } catch (error) {
    if (error instanceof AuthError && error.reason === 'invalid') return null;
    throw error;
  }
}

async function post(path: string, payload?: object): Promise<void> {
  const csrf = await token();
  await request(path, {
    method: 'POST',
    headers: {
      'X-CSRF-TOKEN': csrf,
      ...(payload ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(payload ? { body: JSON.stringify(payload) } : {}),
  });
}

export async function initialize(
  userName: string,
  password: string,
): Promise<void> {
  await post('initialize', { userName, password });
}

export async function login(
  userName: string,
  password: string,
): Promise<string> {
  await post('login', { userName, password });
  const user = await getSession();
  if (user === null) throw new AuthError('unavailable');
  return user;
}

export async function logout(): Promise<void> {
  await post('logout');
}
