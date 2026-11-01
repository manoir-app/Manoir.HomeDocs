export interface AdminUsageContext {
  id: string;
  name: string;
  rootNodeId: string | null;
}

export type ContextFailure =
  | 'invalid'
  | 'sessionExpired'
  | 'notFound'
  | 'invalidRoot'
  | 'conflict'
  | 'unavailable';

export class ContextError extends Error {
  constructor(public readonly reason: ContextFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseContext(value: unknown): AdminUsageContext {
  if (
    !isObject(value) ||
    typeof value.id !== 'string' ||
    typeof value.name !== 'string' ||
    (value.rootNodeId !== null && typeof value.rootNodeId !== 'string')
  ) {
    throw new ContextError('unavailable');
  }
  return {
    id: value.id,
    name: value.name,
    rootNodeId: value.rootNodeId,
  };
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(`/api/admin/contexts${path}`, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new ContextError('unavailable');
  }
  if (!response.ok) {
    if (response.status === 400) throw new ContextError('invalid');
    if (response.status === 401) throw new ContextError('sessionExpired');
    if (response.status === 404) throw new ContextError('notFound');
    if (response.status === 422) throw new ContextError('invalidRoot');
    if (response.status === 409) throw new ContextError('conflict');
    throw new ContextError('unavailable');
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
    throw new ContextError('unavailable');
  }
  if (!response.ok) throw new ContextError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new ContextError('unavailable');
  return data.token;
}

async function write(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: object,
): Promise<Response> {
  return request(path, {
    method,
    headers: {
      'X-CSRF-TOKEN': await token(),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
}

export async function listContexts(): Promise<AdminUsageContext[]> {
  const data: unknown = await (await request('/')).json();
  if (!Array.isArray(data)) throw new ContextError('unavailable');
  return data.map(parseContext);
}

export async function createContext(context: AdminUsageContext): Promise<void> {
  await write('/', 'POST', context);
}

export async function updateContext(
  currentId: string,
  context: AdminUsageContext,
): Promise<void> {
  await write(`/${encodeURIComponent(currentId)}`, 'PUT', context);
}

export async function deleteContext(id: string): Promise<void> {
  await write(`/${encodeURIComponent(id)}`, 'DELETE');
}
