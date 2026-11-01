import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import {
  Button,
  Card,
  Field,
  LoginPage,
  TextField,
} from '@manoir-app/core-admin-ui-kit';
import '@manoir-app/core-admin-ui-kit/styles.css';
import { browserLanguage, translate } from './translations';
import type { Language } from './translations';
import { AdminSettings } from './AdminSettings';
import { adminRouteFromPath, adminRoutePath } from './adminRoutes';
import type { AdminRoute } from './adminRoutes';
import { AdminContent } from './AdminContent';
import { useAdminSettings } from './useAdminSettings';
import { useAdminAuth } from './useAdminAuth';
import './app.css';

export function App() {
  const [languageOverride, setLanguageOverride] =
    useState<Language>(browserLanguage);
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const { state, refresh, submit, signOut } = useAdminAuth();
  const adminSettings = useAdminSettings(state.phase === 'authenticated');
  const [adminRoute, setAdminRoute] = useState<AdminRoute>(() =>
    adminRouteFromPath(window.location.pathname),
  );
  const language = adminSettings.state.settings?.language ?? languageOverride;
  const t = (key: Parameters<typeof translate>[1]) => translate(language, key);

  useEffect(() => {
    document.documentElement.lang = language;
  }, [language]);

  useEffect(() => {
    const updatePage = () =>
      setAdminRoute(adminRouteFromPath(window.location.pathname));
    window.addEventListener('popstate', updatePage);
    return () => window.removeEventListener('popstate', updatePage);
  }, []);

  useEffect(() => {
    if (adminSettings.state.error === 'sessionExpired') {
      queueMicrotask(() => void refresh());
    }
  }, [adminSettings.state.error, refresh]);

  const navigateAdmin = (route: AdminRoute) => {
    const path = adminRoutePath(route);
    window.history.pushState(null, '', path);
    setAdminRoute(route);
  };

  const changeLanguage = async (value: Language) => {
    if (state.phase === 'authenticated') {
      if (adminSettings.state.settings)
        await adminSettings.changeLanguage(value);
    } else {
      setLanguageOverride(value);
    }
  };

  const languagePicker = (
    <div className="language-picker" role="group" aria-label={t('language')}>
      {(['fr', 'en'] as const).map((value) => (
        <button
          key={value}
          type="button"
          lang={value}
          aria-pressed={language === value}
          disabled={
            state.phase === 'authenticated' &&
            (!adminSettings.state.settings || adminSettings.state.busy)
          }
          onClick={() => void changeLanguage(value)}
        >
          {value.toUpperCase()}
        </button>
      ))}
    </div>
  );

  if (state.phase === 'loading') {
    return (
      <main className="mn-kit-theme admin-status" role="status">
        {t('loading')}
      </main>
    );
  }

  if (state.phase === 'error') {
    return (
      <main className="mn-kit-theme admin-status">
        <p role="alert">{t('unavailable')}</p>
        <Button onClick={() => void refresh()}>{t('retry')}</Button>
      </main>
    );
  }

  if (state.phase === 'authenticated') {
    if (adminRoute?.type === 'settings' && adminSettings.state.settings) {
      return (
        <AdminSettings
          page={adminRoute.page}
          settings={adminSettings.state.settings}
          language={language}
          busy={adminSettings.state.busy}
          error={adminSettings.state.error}
          notice={adminSettings.state.notice}
          translate={t}
          onNavigate={(page) => navigateAdmin({ type: 'settings', page })}
          onBack={() => navigateAdmin(null)}
          onLanguageChange={adminSettings.changeLanguage}
          onAccountChange={adminSettings.changeAccount}
          onPinChange={adminSettings.changePin}
        />
      );
    }
    if (
      adminRoute?.type === 'content-list' ||
      adminRoute?.type === 'content-new' ||
      adminRoute?.type === 'content-edit'
    ) {
      return (
        <AdminContent
          key={
            adminRoute.type === 'content-edit' ? adminRoute.id : adminRoute.type
          }
          route={adminRoute}
          translate={t}
          onNavigate={navigateAdmin}
          onSessionExpired={() => refresh()}
        />
      );
    }
    if (adminRoute?.type === 'settings' && !adminSettings.state.settings) {
      return (
        <main className="mn-kit-theme admin-status" role="status">
          {adminSettings.state.error ? t('unavailable') : t('loading')}
          {adminSettings.state.error && (
            <Button onClick={() => void adminSettings.reload()}>
              {t('retry')}
            </Button>
          )}
        </main>
      );
    }
    return (
      <main className="mn-kit-theme admin-home">
        <header className="admin-home-header">
          <strong>{t('app')}</strong>
          {languagePicker}
        </header>
        <Card className="admin-home-content">
          <p>
            {t('signedIn')} :{' '}
            {adminSettings.state.settings?.userName ?? state.userName}
          </p>
          <h1>{t('admin')}</h1>
          <p>{t('ready')}</p>
          {state.notice && <p role="alert">{t(state.notice)}</p>}
          <Button
            variant="secondary"
            onClick={() =>
              navigateAdmin({ type: 'settings', page: 'preferences' })
            }
          >
            {t('openSettings')}
          </Button>
          <Button
            variant="secondary"
            onClick={() => navigateAdmin({ type: 'content-list' })}
          >
            {t('openContent')}
          </Button>
          <Button
            variant="secondary"
            disabled={state.busy}
            onClick={() => void signOut()}
          >
            {t('signOut')}
          </Button>
        </Card>
      </main>
    );
  }

  const isSetup = state.phase === 'setup';
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void submit(userName, password, confirmation).then((succeeded) => {
      if (succeeded) {
        setPassword('');
        setConfirmation('');
      }
    });
  };

  return (
    <main className="mn-kit-theme admin-auth">
      <LoginPage
        topBarBrand={t('app')}
        topBarStatus={languagePicker}
        heroEyebrow={t('admin')}
        heroTitle={<h1>{t('hero')}</h1>}
        heroDescription={t('heroDescription')}
        panelTitle={<h2>{t(isSetup ? 'setupTitle' : 'loginTitle')}</h2>}
        panelDescription={t(isSetup ? 'setupDescription' : 'loginDescription')}
      >
        <form className="auth-form" onSubmit={onSubmit}>
          <Field htmlFor="admin-user" label={t('userName')} required>
            <TextField
              id="admin-user"
              name="username"
              autoComplete="username"
              maxLength={128}
              required
              value={userName}
              onChange={(event) => setUserName(event.target.value)}
            />
          </Field>
          <Field htmlFor="admin-password" label={t('password')} required>
            <TextField
              id="admin-password"
              name="password"
              type="password"
              autoComplete={isSetup ? 'new-password' : 'current-password'}
              minLength={12}
              maxLength={1024}
              aria-describedby={isSetup ? 'password-hint' : undefined}
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </Field>
          {isSetup && (
            <>
              <p className="auth-hint" id="password-hint">
                {t('passwordHint')}
              </p>
              <Field
                htmlFor="admin-confirmation"
                label={t('confirmation')}
                required
              >
                <TextField
                  id="admin-confirmation"
                  name="confirmation"
                  type="password"
                  autoComplete="new-password"
                  minLength={12}
                  maxLength={1024}
                  required
                  value={confirmation}
                  onChange={(event) => setConfirmation(event.target.value)}
                />
              </Field>
            </>
          )}
          {state.notice && (
            <p
              className="auth-notice"
              role={state.notice === 'setupComplete' ? 'status' : 'alert'}
            >
              {t(state.notice)}
            </p>
          )}
          <Button type="submit" size="lg" disabled={state.busy}>
            {t(state.busy ? 'signingIn' : isSetup ? 'create' : 'signIn')}
          </Button>
        </form>
      </LoginPage>
    </main>
  );
}
