export interface ContentPage {
  id: string;
  title: string;
  slug: string;
  category: string;
  contexts: string[];
  requiresAuthentication: boolean;
  isPublished: boolean;
  markdown: string;
  revision: number;
}

export interface ContentPageInput {
  title: string;
  slug: string;
  category: string;
  contexts: string[];
  requiresAuthentication: boolean;
  markdown: string;
}

export type ContentFailure =
  | 'invalid'
  | 'sessionExpired'
  | 'notFound'
  | 'conflict'
  | 'throttled'
  | 'unavailable';

export class ContentError extends Error {
  constructor(public readonly reason: ContentFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parsePage(value: unknown): ContentPage {
  if (
    !isObject(value) ||
    typeof value.id !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.slug !== 'string' ||
    typeof value.category !== 'string' ||
    !Array.isArray(value.contexts) ||
    !value.contexts.every((item): item is string => typeof item === 'string') ||
    typeof value.requiresAuthentication !== 'boolean' ||
    typeof value.isPublished !== 'boolean' ||
    typeof value.markdown !== 'string' ||
    typeof value.revision !== 'number'
  ) {
    throw new ContentError('unavailable');
  }
  return {
    id: value.id,
    title: value.title,
    slug: value.slug,
    category: value.category,
    contexts: value.contexts,
    requiresAuthentication: value.requiresAuthentication,
    isPublished: value.isPublished,
    markdown: value.markdown,
    revision: value.revision,
  };
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin/pages${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new ContentError('unavailable');
  }
  if (!response.ok) {
    if (response.status === 400) throw new ContentError('invalid');
    if (response.status === 401) throw new ContentError('sessionExpired');
    if (response.status === 404) throw new ContentError('notFound');
    if (response.status === 409) throw new ContentError('conflict');
    if (response.status === 429) throw new ContentError('throttled');
    throw new ContentError('unavailable');
  }
  return response;
}

async function token(): Promise<string> {
  let response: Response;
  try {
    response = await fetch('/api/admin/auth/antiforgery', {
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new ContentError('unavailable');
  }
  if (!response.ok) throw new ContentError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new ContentError('unavailable');
  return data.token;
}

async function write(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: object,
): Promise<Response> {
  const csrf = await token();
  return request(path, {
    method,
    headers: {
      'X-CSRF-TOKEN': csrf,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
}

export async function listPages(): Promise<ContentPage[]> {
  const response = await request('/');
  const data: unknown = await response.json();
  if (!Array.isArray(data)) throw new ContentError('unavailable');
  return data.map(parsePage);
}

export async function getPage(id: string): Promise<ContentPage> {
  return parsePage(await (await request(`/${encodeURIComponent(id)}`)).json());
}

export async function createPage(input: ContentPageInput): Promise<string> {
  const response = await write('/', 'POST', input);
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.id !== 'string')
    throw new ContentError('unavailable');
  return data.id;
}

export async function updatePage(
  page: ContentPage,
  input: ContentPageInput,
): Promise<void> {
  await write(`/${encodeURIComponent(page.id)}`, 'PUT', {
    revision: page.revision,
    page: input,
  });
}

export async function setPublication(
  page: ContentPage,
  isPublished: boolean,
): Promise<void> {
  await write(`/${encodeURIComponent(page.id)}/publication`, 'POST', {
    revision: page.revision,
    isPublished,
  });
}

export async function duplicatePage(
  title: string,
  slug: string,
  id: string,
): Promise<string> {
  const response = await write(`/${encodeURIComponent(id)}/duplicate`, 'POST', {
    title,
    slug,
  });
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.id !== 'string')
    throw new ContentError('unavailable');
  return data.id;
}

export async function deletePage(page: ContentPage): Promise<void> {
  await write(
    `/${encodeURIComponent(page.id)}?revision=${encodeURIComponent(String(page.revision))}`,
    'DELETE',
  );
}
