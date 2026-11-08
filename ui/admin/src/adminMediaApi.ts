export interface MediaItem {
  id: string;
  name: string;
  kind: 'image' | 'pdf';
  contentType: string;
  size: number;
  title: string;
  altText: string;
  folder: string;
  createdAt: string;
  revision: number;
  url: string;
  markdown: string;
  usageCount: number;
}

export interface MediaUsage {
  pageId: string;
  title: string;
  slug: string;
  isPublished: boolean;
}

export interface MediaDetail {
  media: MediaItem;
  usages: MediaUsage[];
}

export interface MediaFilter {
  q: string;
  folder: string;
  kind: '' | 'image' | 'pdf';
}

export interface MediaFields {
  name: string;
  title: string;
  altText: string;
  folder: string;
}

export type MediaFailure =
  | 'invalid'
  | 'unsupported'
  | 'tooLarge'
  | 'sessionExpired'
  | 'notFound'
  | 'conflict'
  | 'inUse'
  | 'throttled'
  | 'unavailable';

export class MediaError extends Error {
  constructor(
    public readonly reason: MediaFailure,
    public readonly usages: MediaUsage[] = [],
  ) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseItem(value: unknown): MediaItem {
  if (
    !isObject(value) ||
    typeof value.id !== 'string' ||
    typeof value.name !== 'string' ||
    (value.kind !== 'image' && value.kind !== 'pdf') ||
    typeof value.contentType !== 'string' ||
    typeof value.size !== 'number' ||
    typeof value.title !== 'string' ||
    typeof value.altText !== 'string' ||
    typeof value.folder !== 'string' ||
    typeof value.createdAt !== 'string' ||
    typeof value.revision !== 'number' ||
    typeof value.url !== 'string' ||
    typeof value.markdown !== 'string' ||
    typeof value.usageCount !== 'number'
  ) {
    throw new MediaError('unavailable');
  }
  return {
    id: value.id,
    name: value.name,
    kind: value.kind,
    contentType: value.contentType,
    size: value.size,
    title: value.title,
    altText: value.altText,
    folder: value.folder,
    createdAt: value.createdAt,
    revision: value.revision,
    url: value.url,
    markdown: value.markdown,
    usageCount: value.usageCount,
  };
}

function parseUsages(value: unknown): MediaUsage[] {
  if (!Array.isArray(value)) throw new MediaError('unavailable');
  return value.map((usage: unknown) => {
    if (
      !isObject(usage) ||
      typeof usage.pageId !== 'string' ||
      typeof usage.title !== 'string' ||
      typeof usage.slug !== 'string' ||
      typeof usage.isPublished !== 'boolean'
    ) {
      throw new MediaError('unavailable');
    }
    return {
      pageId: usage.pageId,
      title: usage.title,
      slug: usage.slug,
      isPublished: usage.isPublished,
    };
  });
}

function parseDetail(value: unknown): MediaDetail {
  if (!isObject(value)) throw new MediaError('unavailable');
  return {
    media: parseItem(value.media),
    usages: parseUsages(value.usages),
  };
}

async function fail(response: Response): Promise<never> {
  if (response.status === 400) throw new MediaError('invalid');
  if (response.status === 401) throw new MediaError('sessionExpired');
  if (response.status === 404) throw new MediaError('notFound');
  if (response.status === 413) throw new MediaError('tooLarge');
  if (response.status === 415) throw new MediaError('unsupported');
  if (response.status === 429) throw new MediaError('throttled');
  if (response.status === 409) {
    let usages: MediaUsage[] = [];
    let inUse = false;
    try {
      const data: unknown = await response.json();
      if (isObject(data) && Array.isArray(data.usages)) {
        usages = parseUsages(data.usages);
        inUse = true;
      }
    } catch {
      // A conflict without a readable body is a revision conflict.
    }
    throw new MediaError(inUse ? 'inUse' : 'conflict', usages);
  }
  throw new MediaError('unavailable');
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin/media${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new MediaError('unavailable');
  }
  if (!response.ok) return fail(response);
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
    throw new MediaError('unavailable');
  }
  if (!response.ok) throw new MediaError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new MediaError('unavailable');
  return data.token;
}

export async function listMedia(filter: MediaFilter): Promise<MediaItem[]> {
  const search = new URLSearchParams();
  if (filter.q.trim()) search.set('q', filter.q.trim());
  if (filter.folder) search.set('folder', filter.folder);
  if (filter.kind) search.set('kind', filter.kind);
  const suffix = search.toString();
  const response = await request(suffix ? `/?${suffix}` : '/');
  const data: unknown = await response.json();
  if (!Array.isArray(data)) throw new MediaError('unavailable');
  return data.map(parseItem);
}

export async function getMedia(id: string): Promise<MediaDetail> {
  return parseDetail(
    await (await request(`/${encodeURIComponent(id)}`)).json(),
  );
}

export async function uploadMedia(
  file: File,
  fields: Pick<MediaFields, 'title' | 'altText' | 'folder'>,
): Promise<MediaDetail> {
  const form = new FormData();
  form.append('file', file, file.name);
  if (fields.title) form.append('title', fields.title);
  if (fields.altText) form.append('altText', fields.altText);
  if (fields.folder) form.append('folder', fields.folder);
  const response = await request('/', {
    method: 'POST',
    headers: { 'X-CSRF-TOKEN': await token() },
    body: form,
  });
  return parseDetail(await response.json());
}

export async function updateMedia(
  media: MediaItem,
  fields: MediaFields,
): Promise<MediaDetail> {
  const response = await request(`/${encodeURIComponent(media.id)}`, {
    method: 'PUT',
    headers: {
      'X-CSRF-TOKEN': await token(),
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ revision: media.revision, ...fields }),
  });
  return parseDetail(await response.json());
}

export async function deleteMedia(
  media: MediaItem,
  force: boolean,
): Promise<void> {
  const search = new URLSearchParams({ revision: String(media.revision) });
  if (force) search.set('force', 'true');
  await request(`/${encodeURIComponent(media.id)}?${search.toString()}`, {
    method: 'DELETE',
    headers: { 'X-CSRF-TOKEN': await token() },
  });
}
