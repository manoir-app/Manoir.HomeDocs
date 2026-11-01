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
import { useAdminAuth } from './useAdminAuth';
import './app.css';

export function App() {
  const [language, setLanguage] = useState<Language>(browserLanguage);
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const { state, refresh, submit, signOut } = useAdminAuth();
  const t = (key: Parameters<typeof translate>[1]) => translate(language, key);

  useEffect(() => {
    document.documentElement.lang = language;
  }, [language]);

  const languagePicker = (
    <div className="language-picker" role="group" aria-label={t('language')}>
      {(['fr', 'en'] as const).map((value) => (
        <button
          key={value}
          type="button"
          lang={value}
          aria-pressed={language === value}
          onClick={() => setLanguage(value)}
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
    return (
      <main className="mn-kit-theme admin-home">
        <header className="admin-home-header">
          <strong>{t('app')}</strong>
          {languagePicker}
        </header>
        <Card className="admin-home-content">
          <p>
            {t('signedIn')} : {state.userName}
          </p>
          <h1>{t('admin')}</h1>
          <p>{t('ready')}</p>
          {state.notice && <p role="alert">{t(state.notice)}</p>}
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
