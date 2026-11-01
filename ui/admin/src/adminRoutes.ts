import type { AdminSettingsPage } from './AdminSettings';

export type AdminRoute =
  | { type: 'settings'; page: AdminSettingsPage }
  | { type: 'content-list' }
  | { type: 'content-new' }
  | { type: 'content-edit'; id: string }
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
  return `/admin/pages/${route.id}`;
}
