export type DisplayProfile =
  'desktop' | 'mobile' | 'tablet-wall' | 'tablet-dock' | 'tv';

export const PublicPinIdleTimeoutMs = 30_000;

export interface PublicInvocation {
  context: string | null;
  profile: string | null;
  device: string | null;
}

export interface PublicNavigationItem {
  nodeId: string;
  pageId: string;
  title: string;
  slug: string;
  isLink: boolean;
  requiresAuthentication: boolean;
  children: PublicNavigationItem[];
}

export interface PublicNavigation {
  contextId: string;
  contextName: string;
  profile: DisplayProfile;
  device: string | null;
  root: PublicNavigationItem;
  children: PublicNavigationItem[];
}

export interface PublicPage {
  id: string;
  title: string;
  slug: string;
  category: string;
  contexts: string[];
  markdown: string;
  requiresAuthentication: boolean;
}

export interface PublicSearchResult {
  pageId: string;
  title: string;
  slug: string;
  category: string;
  summary: string;
  excerpt: string | null;
  requiresAuthentication: boolean;
  path: string[];
}

export interface PublicSearchResponse {
  query: string;
  corrected: boolean;
  results: PublicSearchResult[];
}

export type PublicFailure =
  | 'invalidInvocation'
  | 'pinRequired'
  | 'invalidPin'
  | 'notFound'
  | 'rateLimited'
  | 'unavailable';

export class PublicApiError extends Error {
  constructor(public readonly reason: PublicFailure) {
    super(reason);
  }
}

export function isDisplayProfile(
  value: string | null,
): value is DisplayProfile {
  return (
    value === 'desktop' ||
    value === 'mobile' ||
    value === 'tablet-wall' ||
    value === 'tablet-dock' ||
    value === 'tv'
  );
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseNavigationItem(value: unknown): PublicNavigationItem {
  if (
    !isObject(value) ||
    typeof value.nodeId !== 'string' ||
    typeof value.pageId !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.slug !== 'string' ||
    typeof value.isLink !== 'boolean' ||
    typeof value.requiresAuthentication !== 'boolean' ||
    !Array.isArray(value.children)
  ) {
    throw new PublicApiError('unavailable');
  }
  return {
    nodeId: value.nodeId,
    pageId: value.pageId,
    title: value.title,
    slug: value.slug,
    isLink: value.isLink,
    requiresAuthentication: value.requiresAuthentication,
    children: value.children.map(parseNavigationItem),
  };
}

function parseNavigation(value: unknown): PublicNavigation {
  if (
    !isObject(value) ||
    typeof value.contextId !== 'string' ||
    typeof value.contextName !== 'string' ||
    typeof value.profile !== 'string' ||
    !isDisplayProfile(value.profile) ||
    (value.device !== null && typeof value.device !== 'string') ||
    !Array.isArray(value.children)
  ) {
    throw new PublicApiError('unavailable');
  }
  return {
    contextId: value.contextId,
    contextName: value.contextName,
    profile: value.profile,
    device: value.device,
    root: parseNavigationItem(value.root),
    children: value.children.map(parseNavigationItem),
  };
}

function parsePage(value: unknown): PublicPage {
  if (
    !isObject(value) ||
    typeof value.id !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.slug !== 'string' ||
    typeof value.category !== 'string' ||
    !Array.isArray(value.contexts) ||
    !value.contexts.every(
      (context): context is string => typeof context === 'string',
    ) ||
    typeof value.markdown !== 'string' ||
    typeof value.requiresAuthentication !== 'boolean'
  ) {
    throw new PublicApiError('unavailable');
  }
  return {
    id: value.id,
    title: value.title,
    slug: value.slug,
    category: value.category,
    contexts: value.contexts,
    markdown: value.markdown,
    requiresAuthentication: value.requiresAuthentication,
  };
}

function parseSearchResult(value: unknown): PublicSearchResult {
  if (
    !isObject(value) ||
    typeof value.pageId !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.slug !== 'string' ||
    typeof value.category !== 'string' ||
    typeof value.summary !== 'string' ||
    (value.excerpt !== null && typeof value.excerpt !== 'string') ||
    typeof value.requiresAuthentication !== 'boolean' ||
    !Array.isArray(value.path) ||
    !value.path.every((item): item is string => typeof item === 'string')
  ) {
    throw new PublicApiError('unavailable');
  }
  return {
    pageId: value.pageId,
    title: value.title,
    slug: value.slug,
    category: value.category,
    summary: value.summary,
    excerpt: value.excerpt,
    requiresAuthentication: value.requiresAuthentication,
    path: value.path,
  };
}

function parseSearchResponse(value: unknown): PublicSearchResponse {
  if (
    !isObject(value) ||
    typeof value.query !== 'string' ||
    typeof value.corrected !== 'boolean' ||
    !Array.isArray(value.results)
  ) {
    throw new PublicApiError('unavailable');
  }
  return {
    query: value.query,
    corrected: value.corrected,
    results: value.results.map(parseSearchResult),
  };
}

function query(invocation: PublicInvocation): string {
  if (!invocation.context || !isDisplayProfile(invocation.profile))
    throw new PublicApiError('invalidInvocation');
  const search = new URLSearchParams({
    context: invocation.context,
    profile: invocation.profile,
  });
  if (invocation.device) search.set('device', invocation.device);
  return search.toString();
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new PublicApiError('unavailable');
  }
  if (!response.ok) {
    if (response.status === 401) throw new PublicApiError('pinRequired');
    if (response.status === 404) throw new PublicApiError('notFound');
    if (response.status === 429) throw new PublicApiError('rateLimited');
    throw new PublicApiError('unavailable');
  }
  return response;
}

async function antiforgeryToken(): Promise<string> {
  const response = await request('/api/admin/auth/antiforgery');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new PublicApiError('unavailable');
  return data.token;
}

export async function getNavigation(
  invocation: PublicInvocation,
): Promise<PublicNavigation> {
  const response = await request(`/api/public/navigation?${query(invocation)}`);
  return parseNavigation(await response.json());
}

export async function getPage(
  slug: string,
  invocation: PublicInvocation,
): Promise<PublicPage> {
  const response = await request(
    `/api/public/pages/${encodeURIComponent(slug)}?${query(invocation)}`,
  );
  return parsePage(await response.json());
}

export async function searchPages(
  text: string,
  invocation: PublicInvocation,
  signal?: AbortSignal,
): Promise<PublicSearchResponse> {
  const search = new URLSearchParams(query(invocation));
  search.set('q', text);
  const response = await request(`/api/public/search?${search.toString()}`, {
    signal,
  });
  return parseSearchResponse(await response.json());
}

export async function verifyPin(
  invocation: PublicInvocation,
  pin: string,
): Promise<void> {
  const response = await fetch('/api/public/access/pin', {
    method: 'POST',
    cache: 'no-store',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': await antiforgeryToken(),
    },
    body: JSON.stringify({ ...invocation, pin }),
  }).catch(() => {
    throw new PublicApiError('unavailable');
  });
  if (!response.ok) {
    if (response.status === 401) throw new PublicApiError('invalidPin');
    if (response.status === 404) throw new PublicApiError('notFound');
    if (response.status === 429) throw new PublicApiError('rateLimited');
    throw new PublicApiError('unavailable');
  }
}

export async function refreshProtectedAccess(
  invocation: PublicInvocation,
): Promise<boolean> {
  let response: Response;
  try {
    response = await fetch(`/api/public/access/status?${query(invocation)}`, {
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new PublicApiError('unavailable');
  }
  if (response.status === 401) return false;
  if (!response.ok) {
    if (response.status === 404) throw new PublicApiError('notFound');
    throw new PublicApiError('unavailable');
  }
  return true;
}

export async function logoutProtectedAccess(): Promise<void> {
  const response = await fetch('/api/public/access/logout', {
    method: 'POST',
    cache: 'no-store',
    credentials: 'same-origin',
    headers: { 'X-CSRF-TOKEN': await antiforgeryToken() },
  }).catch(() => {
    throw new PublicApiError('unavailable');
  });
  if (!response.ok) throw new PublicApiError('unavailable');
}
