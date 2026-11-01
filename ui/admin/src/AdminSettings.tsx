import { useState } from 'react';
import type { FormEvent } from 'react';
import { Button, Card, Field, TextField } from '@manoir-app/core-admin-ui-kit';
import type { AdminSettings } from './adminSettingsApi';
import type { SettingsFailure } from './adminSettingsApi';
import type { Language, TranslationKey } from './translations';

export type AdminSettingsPage =
  'preferences' | 'security' | 'homeAssistant' | 'otherIntegrations';

interface Props {
  page: AdminSettingsPage;
  settings: AdminSettings;
  language: Language;
  busy: boolean;
  error?: SettingsFailure;
  notice?: 'saved';
  translate: (key: TranslationKey) => string;
  onNavigate: (page: AdminSettingsPage) => void;
  onBack: () => void;
  onLanguageChange: (language: Language) => Promise<boolean>;
  onAccountChange: (
    currentPassword: string,
    userName: string,
    newPassword: string | null,
  ) => Promise<boolean>;
  onPinChange: (
    currentPassword: string,
    pin: string | null,
  ) => Promise<boolean>;
}

export function AdminSettings({
  page,
  settings,
  language,
  busy,
  error,
  notice,
  translate: t,
  onNavigate,
  onBack,
  onLanguageChange,
  onAccountChange,
  onPinChange,
}: Props) {
  const pages: { id: AdminSettingsPage; label: TranslationKey }[] = [
    { id: 'preferences', label: 'uiPreferences' },
    { id: 'security', label: 'security' },
    { id: 'homeAssistant', label: 'homeAssistant' },
    { id: 'otherIntegrations', label: 'otherIntegrations' },
  ];

  return (
    <main className="mn-kit-theme settings-page">
      <header className="admin-home-header">
        <strong>{t('app')}</strong>
        <Button variant="secondary" onClick={onBack}>
          {t('backToAdmin')}
        </Button>
      </header>
      <div className="settings-layout">
        <nav className="settings-nav" aria-label={t('settings')}>
          <h1>{t('settings')}</h1>
          {pages.map((item) => (
            <button
              key={item.id}
              type="button"
              aria-current={page === item.id ? 'page' : undefined}
              onClick={() => onNavigate(item.id)}
            >
              {t(item.label)}
            </button>
          ))}
        </nav>
        <section className="settings-content" aria-live="polite">
          <h2>{t(pages.find((item) => item.id === page)!.label)}</h2>
          <p>{t('settingsDescription')}</p>
          {error && (
            <p className="auth-notice" role="alert">
              {t(settingsErrorKey(error))}
            </p>
          )}
          {notice && (
            <p className="auth-notice" role="status">
              {t('saved')}
            </p>
          )}
          {page === 'preferences' && (
            <Preferences
              language={language}
              busy={busy}
              t={t}
              onChange={onLanguageChange}
            />
          )}
          {page === 'security' && (
            <Security
              key={settings.userName}
              settings={settings}
              busy={busy}
              t={t}
              onAccountChange={onAccountChange}
              onPinChange={onPinChange}
            />
          )}
          {page === 'homeAssistant' && (
            <Card className="settings-card">
              <h3>{t('homeAssistant')}</h3>
              <p>{t('integrationDeferred')}</p>
              <p>{t('noSecretStored')}</p>
            </Card>
          )}
          {page === 'otherIntegrations' && (
            <Card className="settings-card">
              <h3>{t('otherIntegrations')}</h3>
              <p>{t('otherIntegrationDeferred')}</p>
            </Card>
          )}
        </section>
      </div>
    </main>
  );
}

function Preferences({
  language,
  busy,
  t,
  onChange,
}: {
  language: Language;
  busy: boolean;
  t: (key: TranslationKey) => string;
  onChange: (language: Language) => Promise<boolean>;
}) {
  return (
    <Card className="settings-card">
      <h3>{t('preferredLanguage')}</h3>
      <div
        className="language-picker"
        role="group"
        aria-label={t('preferredLanguage')}
      >
        {(['fr', 'en'] as const).map((value) => (
          <Button
            key={value}
            type="button"
            variant={language === value ? 'primary' : 'secondary'}
            aria-pressed={language === value}
            disabled={busy || language === value}
            onClick={() => void onChange(value)}
          >
            {value.toUpperCase()}
          </Button>
        ))}
      </div>
    </Card>
  );
}

function Security({
  settings,
  busy,
  t,
  onAccountChange,
  onPinChange,
}: {
  settings: AdminSettings;
  busy: boolean;
  t: (key: TranslationKey) => string;
  onAccountChange: Props['onAccountChange'];
  onPinChange: Props['onPinChange'];
}) {
  const [userName, setUserName] = useState(settings.userName);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [accountError, setAccountError] = useState<'passwordMismatch'>();
  const [pinCurrentPassword, setPinCurrentPassword] = useState('');
  const [pin, setPin] = useState('');
  const [confirmPin, setConfirmPin] = useState('');
  const [pinError, setPinError] = useState<'pinMismatch'>();

  const saveAccount = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (newPassword !== confirmPassword) {
      setAccountError('passwordMismatch');
      return;
    }
    setAccountError(undefined);
    void onAccountChange(currentPassword, userName, newPassword || null).then(
      (success) => {
        if (success) {
          setCurrentPassword('');
          setNewPassword('');
          setConfirmPassword('');
        }
      },
    );
  };

  const savePin = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pin !== confirmPin) {
      setPinError('pinMismatch');
      return;
    }
    setPinError(undefined);
    void onPinChange(pinCurrentPassword, pin).then((success) => {
      if (success) {
        setPinCurrentPassword('');
        setPin('');
        setConfirmPin('');
      }
    });
  };

  const removePin = () => {
    setPinError(undefined);
    void onPinChange(pinCurrentPassword, null).then((success) => {
      if (success) {
        setPinCurrentPassword('');
        setPin('');
        setConfirmPin('');
      }
    });
  };

  return (
    <div className="settings-forms">
      <Card className="settings-card">
        <h3>{t('account')}</h3>
        <form className="auth-form" onSubmit={saveAccount}>
          <Field
            htmlFor="settings-username"
            label={t('currentUserName')}
            required
          >
            <TextField
              id="settings-username"
              autoComplete="username"
              maxLength={128}
              required
              value={userName}
              onChange={(event) => setUserName(event.target.value)}
            />
          </Field>
          <Field
            htmlFor="settings-current-password"
            label={t('currentPassword')}
            required
          >
            <TextField
              id="settings-current-password"
              type="password"
              autoComplete="current-password"
              required
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
            />
          </Field>
          <Field htmlFor="settings-new-password" label={t('newPassword')}>
            <TextField
              id="settings-new-password"
              type="password"
              autoComplete="new-password"
              minLength={12}
              maxLength={1024}
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
            />
          </Field>
          <Field
            htmlFor="settings-confirm-password"
            label={t('confirmNewPassword')}
          >
            <TextField
              id="settings-confirm-password"
              type="password"
              autoComplete="new-password"
              minLength={12}
              maxLength={1024}
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
            />
          </Field>
          {accountError && <p role="alert">{t(accountError)}</p>}
          <Button type="submit" disabled={busy}>
            {t('save')}
          </Button>
        </form>
      </Card>
      <Card className="settings-card">
        <h3>{t('pinTitle')}</h3>
        <p>
          {t(settings.pinConfigured ? 'pinConfigured' : 'pinNotConfigured')}
        </p>
        <p>{t('pinDescription')}</p>
        <form className="auth-form" onSubmit={savePin}>
          <Field
            htmlFor="settings-pin-current-password"
            label={t('currentPassword')}
            required
          >
            <TextField
              id="settings-pin-current-password"
              type="password"
              autoComplete="current-password"
              required
              value={pinCurrentPassword}
              onChange={(event) => setPinCurrentPassword(event.target.value)}
            />
          </Field>
          <Field htmlFor="settings-pin" label={t('pin')} required>
            <TextField
              id="settings-pin"
              type="password"
              inputMode="numeric"
              pattern="[0-9]{6}"
              minLength={6}
              maxLength={6}
              autoComplete="new-password"
              required
              value={pin}
              onChange={(event) => setPin(event.target.value)}
            />
          </Field>
          <Field
            htmlFor="settings-confirm-pin"
            label={t('confirmPin')}
            required
          >
            <TextField
              id="settings-confirm-pin"
              type="password"
              inputMode="numeric"
              pattern="[0-9]{6}"
              minLength={6}
              maxLength={6}
              autoComplete="new-password"
              required
              value={confirmPin}
              onChange={(event) => setConfirmPin(event.target.value)}
            />
          </Field>
          {pinError && <p role="alert">{t(pinError)}</p>}
          <div className="settings-actions">
            <Button type="submit" disabled={busy}>
              {t('savePin')}
            </Button>
            {settings.pinConfigured && (
              <Button
                type="button"
                variant="secondary"
                disabled={busy}
                onClick={removePin}
              >
                {t('removePin')}
              </Button>
            )}
          </div>
        </form>
      </Card>
    </div>
  );
}

function settingsErrorKey(error: SettingsFailure): TranslationKey {
  if (error === 'invalid') return 'invalidSettings';
  if (error === 'sessionExpired') return 'sessionExpired';
  if (error === 'conflict') return 'settingsConflict';
  if (error === 'throttled') return 'settingsThrottled';
  return 'unavailable';
}
