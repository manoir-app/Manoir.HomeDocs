import type { Language } from './translations';

export interface AdminSettings {
  userName: string;
  pinConfigured: boolean;
  language: Language;
}

export type SettingsFailure =
  'invalid' | 'sessionExpired' | 'conflict' | 'throttled' | 'unavailable';

export class SettingsError extends Error {
  constructor(public readonly reason: SettingsFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin/settings${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new SettingsError('unavailable');
  }
  if (!response.ok) {
    if (response.status === 400) throw new SettingsError('invalid');
    if (response.status === 401) throw new SettingsError('sessionExpired');
    if (response.status === 409) throw new SettingsError('conflict');
    if (response.status === 429) throw new SettingsError('throttled');
    throw new SettingsError('unavailable');
  }
  return response;
}

export async function getSettings(): Promise<AdminSettings> {
  const response = await request('/');
  const data: unknown = await response.json();
  if (
    !isObject(data) ||
    typeof data.userName !== 'string' ||
    !data.userName ||
    typeof data.pinConfigured !== 'boolean' ||
    (data.language !== 'fr' && data.language !== 'en')
  ) {
    throw new SettingsError('unavailable');
  }
  return {
    userName: data.userName,
    pinConfigured: data.pinConfigured,
    language: data.language,
  };
}

async function token(): Promise<string> {
  let response: Response;
  try {
    response = await fetch('/api/admin/auth/antiforgery', {
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new SettingsError('unavailable');
  }
  if (!response.ok) throw new SettingsError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token) {
    throw new SettingsError('unavailable');
  }
  return data.token;
}

async function update(path: string, payload: object): Promise<void> {
  await request(path, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': await token(),
    },
    body: JSON.stringify(payload),
  });
}

export async function updateLanguage(language: Language): Promise<void> {
  await update('/language', { language });
}

export async function updateAccount(
  currentPassword: string,
  userName: string,
  newPassword: string | null,
): Promise<void> {
  await update('/account', { currentPassword, userName, newPassword });
}

export async function updatePin(
  currentPassword: string,
  pin: string | null,
): Promise<void> {
  await update('/pin', { currentPassword, pin });
}
