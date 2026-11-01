export type DemoInstallStatus = 'installed' | 'alreadyInstalled';
export type DemoInstallFailure = 'sessionExpired' | 'conflict' | 'unavailable';

export class DemoInstallError extends Error {
  constructor(public readonly reason: DemoInstallFailure) {
    super(reason);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

async function antiforgeryToken(): Promise<string> {
  let response: Response;
  try {
    response = await fetch('/api/admin/auth/antiforgery', {
      cache: 'no-store',
      credentials: 'same-origin',
    });
  } catch {
    throw new DemoInstallError('unavailable');
  }
  if (response.status === 401) throw new DemoInstallError('sessionExpired');
  if (!response.ok) throw new DemoInstallError('unavailable');
  const data: unknown = await response.json();
  if (!isObject(data) || typeof data.token !== 'string' || !data.token)
    throw new DemoInstallError('unavailable');
  return data.token;
}

export async function installDemoData(): Promise<DemoInstallStatus> {
  let response: Response;
  try {
    response = await fetch('/api/admin/demo/install', {
      method: 'POST',
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { 'X-CSRF-TOKEN': await antiforgeryToken() },
    });
  } catch (reason) {
    if (reason instanceof DemoInstallError) throw reason;
    throw new DemoInstallError('unavailable');
  }
  if (response.status === 401) throw new DemoInstallError('sessionExpired');
  if (response.status === 409) throw new DemoInstallError('conflict');
  if (!response.ok) throw new DemoInstallError('unavailable');
  const result: unknown = await response.json();
  if (
    !isObject(result) ||
    (result.status !== 'installed' && result.status !== 'alreadyInstalled')
  ) {
    throw new DemoInstallError('unavailable');
  }
  return result.status;
}
