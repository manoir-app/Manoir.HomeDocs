export interface VersionSummary {
  revision: number;
  event: string;
  title: string;
  slug: string;
  isPublished: boolean;
  requiresAuthentication: boolean;
  savedAt: string;
  size: number;
  restoredFromRevision: number | null;
}

export interface Version {
  summary: VersionSummary;
  category: string;
  contexts: string[];
  markdown: string;
}

export interface PageLink {
  href: string;
  kind: 'page' | 'media' | 'external';
  status: 'ok' | 'redirected' | 'draft' | 'broken' | 'unchecked';
  targetId: string | null;
  targetTitle: string | null;
}

export interface IncomingLink {
  pageId: string;
  title: string;
  slug: string;
  isPublished: boolean;
}

export interface LinkReport {
  outgoing: PageLink[];
  incoming: IncomingLink[];
}

export type HistoryFailure =
  'sessionExpired' | 'notFound' | 'conflict' | 'invalid' | 'unavailable';

export class HistoryError extends Error {
  constructor(public readonly reason: HistoryFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseSummary(value: unknown): VersionSummary {
  if (
    !isObject(value) ||
    typeof value.revision !== 'number' ||
    typeof value.event !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.slug !== 'string' ||
    typeof value.isPublished !== 'boolean' ||
    typeof value.requiresAuthentication !== 'boolean' ||
    typeof value.savedAt !== 'string' ||
    typeof value.size !== 'number' ||
    (value.restoredFromRevision !== null &&
      typeof value.restoredFromRevision !== 'number')
  ) {
    throw new HistoryError('unavailable');
  }
  return {
    revision: value.revision,
    event: value.event,
    title: value.title,
    slug: value.slug,
    isPublished: value.isPublished,
    requiresAuthentication: value.requiresAuthentication,
    savedAt: value.savedAt,
    size: value.size,
    restoredFromRevision: value.restoredFromRevision,
  };
}

function parseVersion(value: unknown): Version {
  if (
    !isObject(value) ||
    typeof value.category !== 'string' ||
    !Array.isArray(value.contexts) ||
    !value.contexts.every((item): item is string => typeof item === 'string') ||
    typeof value.markdown !== 'string'
  ) {
    throw new HistoryError('unavailable');
  }
  return {
    summary: parseSummary(value.summary),
    category: value.category,
    contexts: value.contexts,
    markdown: value.markdown,
  };
}

const linkKinds = ['page', 'media', 'external'] as const;
const linkStatuses = [
  'ok',
  'redirected',
  'draft',
  'broken',
  'unchecked',
] as const;

function parseLinkReport(value: unknown): LinkReport {
  if (
    !isObject(value) ||
    !Array.isArray(value.outgoing) ||
    !Array.isArray(value.incoming)
  ) {
    throw new HistoryError('unavailable');
  }
  return {
    outgoing: value.outgoing.map((link: unknown): PageLink => {
      if (
        !isObject(link) ||
        typeof link.href !== 'string' ||
        !linkKinds.some((kind) => kind === link.kind) ||
        !linkStatuses.some((status) => status === link.status) ||
        (link.targetId !== null && typeof link.targetId !== 'string') ||
        (link.targetTitle !== null && typeof link.targetTitle !== 'string')
      ) {
        throw new HistoryError('unavailable');
      }
      return {
        href: link.href,
        kind: link.kind as PageLink['kind'],
        status: link.status as PageLink['status'],
        targetId: link.targetId,
        targetTitle: link.targetTitle,
      };
    }),
    incoming: value.incoming.map((link: unknown): IncomingLink => {
      if (
        !isObject(link) ||
        typeof link.pageId !== 'string' ||
        typeof link.title !== 'string' ||
        typeof link.slug !== 'string' ||
        typeof link.isPublished !== 'boolean'
      ) {
        throw new HistoryError('unavailable');
      }
      return {
        pageId: link.pageId,
        title: link.title,
        slug: link.slug,
        isPublished: link.isPublished,
      };
    }),
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
    throw new HistoryError('unavailable');
  }
  if (!response.ok) {
    if (response.status === 400) throw new HistoryError('invalid');
    if (response.status === 401) throw new HistoryError('sessionExpired');
    if (response.status === 404) throw new HistoryError('notFound');
    if (response.status === 409) throw new HistoryError('conflict');
    throw new HistoryError('unavailable');
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
    throw new HistoryError('unavailable');
  }
  if (!response.ok) throw new HistoryError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new HistoryError('unavailable');
  return data.token;
}

export async function listVersions(pageId: string): Promise<VersionSummary[]> {
  const data: unknown = await (
    await request(`/${encodeURIComponent(pageId)}/versions`)
  ).json();
  if (!Array.isArray(data)) throw new HistoryError('unavailable');
  return data.map(parseSummary);
}

export async function getVersion(
  pageId: string,
  revision: number,
): Promise<Version> {
  return parseVersion(
    await (
      await request(`/${encodeURIComponent(pageId)}/versions/${revision}`)
    ).json(),
  );
}

export async function restoreVersion(
  pageId: string,
  currentRevision: number,
  versionRevision: number,
): Promise<void> {
  await request(
    `/${encodeURIComponent(pageId)}/versions/${versionRevision}/restore`,
    {
      method: 'POST',
      headers: {
        'X-CSRF-TOKEN': await token(),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ revision: currentRevision }),
    },
  );
}

export async function getLinks(pageId: string): Promise<LinkReport> {
  return parseLinkReport(
    await (await request(`/${encodeURIComponent(pageId)}/links`)).json(),
  );
}
