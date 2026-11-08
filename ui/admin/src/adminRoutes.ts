import type { AdminSettingsPage } from './AdminSettings';

export type AdminRoute =
  | { type: 'settings'; page: AdminSettingsPage }
  | { type: 'content-list' }
  | { type: 'content-new' }
  | { type: 'content-edit'; id: string }
  | { type: 'tree' }
  | { type: 'contexts' }
  | { type: 'media' }
  | null;

export function adminRouteFromPath(path: string): AdminRoute {
  const segments = path.split('/');
  if (segments[2] === 'settings') {
    const page = segments[3];
    if (page === 'preferences') return { type: 'settings', page };
    if (page === 'security') return { type: 'settings', page };
    if (page === 'home-assistant')
      return { type: 'settings', page: 'homeAssistant' };
    if (page === 'integrations')
      return { type: 'settings', page: 'otherIntegrations' };
  }
  if (segments[2] === 'pages') {
    if (!segments[3]) return { type: 'content-list' };
    if (segments[3] === 'new') return { type: 'content-new' };
    if (
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
        segments[3],
      )
    )
      return { type: 'content-edit', id: segments[3] };
  }
  if (segments[2] === 'tree') return { type: 'tree' };
  if (segments[2] === 'contexts') return { type: 'contexts' };
  if (segments[2] === 'media') return { type: 'media' };
  return null;
}

export function adminRoutePath(route: AdminRoute): string {
  if (!route) return '/admin/';
  if (route.type === 'settings') {
    const page =
      route.page === 'homeAssistant'
        ? 'home-assistant'
        : route.page === 'otherIntegrations'
          ? 'integrations'
          : route.page;
    return `/admin/settings/${page}`;
  }
  if (route.type === 'content-list') return '/admin/pages';
  if (route.type === 'content-new') return '/admin/pages/new';
  if (route.type === 'content-edit') return `/admin/pages/${route.id}`;
  if (route.type === 'tree') return '/admin/tree';
  if (route.type === 'contexts') return '/admin/contexts';
  if (route.type === 'media') return '/admin/media';
  return '/admin/';
}
