export interface AdminTreeNode {
  id: string;
  parentNodeId: string | null;
  pageId: string;
  title: string;
  slug: string;
  isLink: boolean;
  position: number;
}

export type TreeFailure =
  'invalid' | 'sessionExpired' | 'notFound' | 'conflict' | 'unavailable';

export class TreeError extends Error {
  constructor(public readonly reason: TreeFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseNode(value: unknown): AdminTreeNode {
  if (
    !isObject(value) ||
    typeof value.id !== 'string' ||
    (value.parentNodeId !== null && typeof value.parentNodeId !== 'string') ||
    typeof value.pageId !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.slug !== 'string' ||
    typeof value.isLink !== 'boolean' ||
    typeof value.position !== 'number'
  ) {
    throw new TreeError('unavailable');
  }
  return {
    id: value.id,
    parentNodeId: value.parentNodeId,
    pageId: value.pageId,
    title: value.title,
    slug: value.slug,
    isLink: value.isLink,
    position: value.position,
  };
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin/tree${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new TreeError('unavailable');
  }
  if (!response.ok) {
    if (response.status === 400) throw new TreeError('invalid');
    if (response.status === 401) throw new TreeError('sessionExpired');
    if (response.status === 404) throw new TreeError('notFound');
    if (response.status === 409) throw new TreeError('conflict');
    throw new TreeError('unavailable');
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
    throw new TreeError('unavailable');
  }
  if (!response.ok) throw new TreeError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new TreeError('unavailable');
  return data.token;
}

async function write(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: object,
): Promise<void> {
  await request(path, {
    method,
    headers: {
      'X-CSRF-TOKEN': await token(),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
}

export async function listTree(): Promise<AdminTreeNode[]> {
  const data: unknown = await (await request('/')).json();
  if (!Array.isArray(data)) throw new TreeError('unavailable');
  return data.map(parseNode);
}

export async function createLink(
  pageId: string,
  parentNodeId: string | null,
): Promise<void> {
  await write('/links', 'POST', { pageId, parentNodeId });
}

export async function moveNode(
  nodeId: string,
  parentNodeId: string | null,
  position: number,
): Promise<void> {
  await write(`/nodes/${encodeURIComponent(nodeId)}`, 'PUT', {
    parentNodeId,
    position,
  });
}

export async function removeNode(nodeId: string): Promise<void> {
  await write(`/nodes/${encodeURIComponent(nodeId)}`, 'DELETE');
}
