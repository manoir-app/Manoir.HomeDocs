export interface ThemeColors {
  background: string;
  surface: string;
  text: string;
  mutedText: string;
  accent: string;
  accentText: string;
  border: string;
  focus: string;
}

export interface ThemeStyle {
  fontFamily: string;
  radiusPx: number;
  shadow: string;
  iconSizePx: number;
}

export interface Theme {
  id: string;
  name: string;
  colorScheme: 'light' | 'dark';
  colors: ThemeColors;
  style: ThemeStyle;
  isBuiltIn: boolean;
  revision: number;
}

export interface ThemeInput {
  id: string;
  name: string;
  colorScheme: 'light' | 'dark';
  colors: ThemeColors;
  style: ThemeStyle;
}

export interface ThemeSelection {
  mode: 'system' | 'light' | 'dark';
  lightThemeId: string;
  darkThemeId: string;
}

export interface ProfileLayout {
  fontSizePx: number;
  typeScale: number;
  spacingScale: number;
  maxColumns: number;
  cardSize: string;
  interactionMode: string;
  minTargetPx: number;
  contentMaxWidthPx: number;
  showTouchActions: boolean;
  showPresentation: boolean;
}

export interface ProfileSettings {
  profileId: string;
  theme: ThemeSelection;
  layout: ProfileLayout;
  isCustomized: boolean;
  revision: number;
}

export type AppearanceFailure =
  | 'invalid'
  | 'sessionExpired'
  | 'notFound'
  | 'conflict'
  | 'inUse'
  | 'builtIn'
  | 'unavailable';

export class AppearanceError extends Error {
  constructor(public readonly reason: AppearanceFailure) {
    super(reason);
  }
}

const colorKeys: (keyof ThemeColors)[] = [
  'background',
  'surface',
  'text',
  'mutedText',
  'accent',
  'accentText',
  'border',
  'focus',
];

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseTheme(value: unknown): Theme {
  if (
    !isObject(value) ||
    typeof value.id !== 'string' ||
    typeof value.name !== 'string' ||
    (value.colorScheme !== 'light' && value.colorScheme !== 'dark') ||
    !isObject(value.colors) ||
    !isObject(value.style) ||
    typeof value.isBuiltIn !== 'boolean' ||
    typeof value.revision !== 'number' ||
    colorKeys.some(
      (key) => typeof (value.colors as object)[key as never] !== 'string',
    ) ||
    typeof value.style.fontFamily !== 'string' ||
    typeof value.style.radiusPx !== 'number' ||
    typeof value.style.shadow !== 'string' ||
    typeof value.style.iconSizePx !== 'number'
  )
    throw new AppearanceError('unavailable');
  return value as unknown as Theme;
}

function parseProfile(value: unknown): ProfileSettings {
  if (
    !isObject(value) ||
    typeof value.profileId !== 'string' ||
    !isObject(value.theme) ||
    !isObject(value.layout) ||
    typeof value.isCustomized !== 'boolean' ||
    typeof value.revision !== 'number' ||
    typeof value.theme.lightThemeId !== 'string' ||
    typeof value.theme.darkThemeId !== 'string' ||
    typeof value.layout.fontSizePx !== 'number' ||
    typeof value.layout.cardSize !== 'string' ||
    typeof value.layout.interactionMode !== 'string'
  )
    throw new AppearanceError('unavailable');
  return value as unknown as ProfileSettings;
}

async function fail(response: Response): Promise<never> {
  if (response.status === 400) throw new AppearanceError('invalid');
  if (response.status === 401) throw new AppearanceError('sessionExpired');
  if (response.status === 403) throw new AppearanceError('builtIn');
  if (response.status === 404) throw new AppearanceError('notFound');
  if (response.status === 409) {
    let reason: AppearanceFailure = 'conflict';
    try {
      const data: unknown = await response.json();
      if (isObject(data) && data.error === 'InUse') reason = 'inUse';
    } catch {
      // A conflict without a readable body is a revision conflict.
    }
    throw new AppearanceError(reason);
  }
  throw new AppearanceError('unavailable');
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new AppearanceError('unavailable');
  }
  if (!response.ok) return fail(response);
  return response;
}

async function write(
  method: string,
  path: string,
  body?: unknown,
): Promise<Response> {
  let tokenResponse: Response;
  try {
    tokenResponse = await fetch('/api/admin/auth/antiforgery', {
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new AppearanceError('unavailable');
  }
  if (!tokenResponse.ok) throw new AppearanceError('unavailable');
  const data: unknown = await tokenResponse.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new AppearanceError('unavailable');
  const headers: Record<string, string> = { 'X-CSRF-TOKEN': data.token };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  return request(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

async function array<T>(
  response: Response,
  parse: (value: unknown) => T,
): Promise<T[]> {
  const data: unknown = await response.json();
  if (!Array.isArray(data)) throw new AppearanceError('unavailable');
  return data.map(parse);
}

export async function listThemes(): Promise<Theme[]> {
  return array(await request('/themes/'), parseTheme);
}

export async function listProfiles(): Promise<ProfileSettings[]> {
  return array(await request('/profiles/'), parseProfile);
}

export async function createTheme(input: ThemeInput): Promise<Theme> {
  return parseTheme(await (await write('POST', '/themes/', input)).json());
}

export async function updateTheme(
  theme: Theme,
  input: ThemeInput,
): Promise<Theme> {
  const response = await write(
    'PUT',
    `/themes/${encodeURIComponent(theme.id)}?revision=${theme.revision}`,
    input,
  );
  return parseTheme(await response.json());
}

export async function deleteTheme(theme: Theme): Promise<void> {
  await write(
    'DELETE',
    `/themes/${encodeURIComponent(theme.id)}?revision=${theme.revision}`,
  );
}

export async function saveProfile(
  profile: ProfileSettings,
  theme: ThemeSelection,
  layout: ProfileLayout,
): Promise<ProfileSettings> {
  const response = await write(
    'PUT',
    `/profiles/${encodeURIComponent(profile.profileId)}?revision=${profile.isCustomized ? profile.revision : 0}`,
    { theme, layout },
  );
  return parseProfile(await response.json());
}

export async function resetProfile(
  profile: ProfileSettings,
): Promise<ProfileSettings> {
  const response = await write(
    'DELETE',
    `/profiles/${encodeURIComponent(profile.profileId)}`,
  );
  return parseProfile(await response.json());
}
