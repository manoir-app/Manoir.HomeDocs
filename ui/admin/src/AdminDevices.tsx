import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Button, Card } from '@manoir-app/core-admin-ui-kit';
import * as api from './adminAppearanceApi';
import type {
  Device,
  DeviceInput,
  DeviceOverride,
  Theme,
} from './adminAppearanceApi';
import type { TranslationKey } from './translations';

type Translate = (key: TranslationKey) => string;

const profileIds = ['desktop', 'mobile', 'tablet-wall', 'tablet-dock', 'tv'];

const numericFields: {
  key:
    | 'fontSizePx'
    | 'typeScale'
    | 'spacingScale'
    | 'maxColumns'
    | 'minTargetPx'
    | 'contentMaxWidthPx';
  label: TranslationKey;
  step: number;
}[] = [
  { key: 'fontSizePx', label: 'layoutFontSize', step: 1 },
  { key: 'typeScale', label: 'layoutTypeScale', step: 0.05 },
  { key: 'spacingScale', label: 'layoutSpacing', step: 0.1 },
  { key: 'maxColumns', label: 'layoutColumns', step: 1 },
  { key: 'minTargetPx', label: 'layoutMinTarget', step: 1 },
  { key: 'contentMaxWidthPx', label: 'layoutContentWidth', step: 10 },
];

function emptyDraft(): DeviceInput {
  return { id: '', name: '', isActive: true, override: {}, customCss: '' };
}

function toInput(device: Device): DeviceInput {
  return {
    id: device.id,
    name: device.name,
    isActive: device.isActive,
    override: { ...device.override },
    customCss: device.customCss,
  };
}

function slugify(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 128);
}

export function AdminDevices({
  translate: t,
  themes,
  onFailure,
  onThemesChanged,
}: {
  translate: Translate;
  themes: Theme[];
  onFailure: (reason: unknown) => void;
  onThemesChanged: () => void;
}) {
  const [devices, setDevices] = useState<Device[]>([]);
  const [selectedId, setSelectedId] = useState<string>();
  const [draft, setDraft] = useState<DeviceInput>();
  const [isNew, setIsNew] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<TranslationKey>();
  const requestId = useRef(0);

  const load = useCallback(async () => {
    const id = ++requestId.current;
    try {
      const loaded = await api.listDevices();
      if (id === requestId.current) setDevices(loaded);
    } catch (reason) {
      if (id === requestId.current) onFailure(reason);
    }
  }, [onFailure]);

  useEffect(() => {
    const active = requestId;
    queueMicrotask(() => void load());
    return () => {
      active.current++;
    };
  }, [load]);

  const selected = devices.find((item) => item.id === selectedId);

  const setOverride = (patch: Partial<DeviceOverride>) =>
    draft && setDraft({ ...draft, override: { ...draft.override, ...patch } });

  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (!draft) return;
    setBusy(true);
    try {
      const input = { ...draft, id: isNew ? slugify(draft.name) : draft.id };
      const saved =
        isNew || !selected
          ? await api.createDevice(input)
          : await api.updateDevice(selected, input);
      setDevices(await api.listDevices());
      setSelectedId(saved.id);
      setDraft(toInput(saved));
      setIsNew(false);
      setNotice('deviceSaved');
    } catch (reason) {
      setNotice(undefined);
      onFailure(reason);
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!selected || !window.confirm(t('deviceDeleteConfirm'))) return;
    setBusy(true);
    try {
      await api.deleteDevice(selected);
      setDevices(await api.listDevices());
      setSelectedId(undefined);
      setDraft(undefined);
      setNotice('deviceDeleted');
      onThemesChanged();
    } catch (reason) {
      setNotice(undefined);
      onFailure(reason);
    } finally {
      setBusy(false);
    }
  };

  const themeOptions = (scheme: 'light' | 'dark') => [
    <option key="" value="">
      {t('deviceInherit')}
    </option>,
    ...themes
      .filter((item) => item.colorScheme === scheme)
      .map((item) => (
        <option key={item.id} value={item.id}>
          {item.name}
        </option>
      )),
  ];

  const exampleLink = draft?.id
    ? `/?context=…&profile=${profileIds[0]}&device=${draft.id}`
    : undefined;

  return (
    <Card className="appearance-card">
      <h2>{t('devicesTitle')}</h2>
      <p>{t('devicesDescription')}</p>
      {notice && <p role="status">{t(notice)}</p>}
      {devices.length === 0 && <p>{t('devicesNone')}</p>}
      <ul className="appearance-themes">
        {devices.map((item) => (
          <li key={item.id}>
            <button
              type="button"
              aria-current={item.id === selectedId}
              onClick={() => {
                setSelectedId(item.id);
                setDraft(toInput(item));
                setIsNew(false);
                setNotice(undefined);
              }}
            >
              {item.name}
              <small>
                {item.isActive ? t('deviceActive') : t('deviceInactive')}
              </small>
            </button>
          </li>
        ))}
      </ul>
      <Button
        variant="secondary"
        onClick={() => {
          setSelectedId(undefined);
          setDraft(emptyDraft());
          setIsNew(true);
          setNotice(undefined);
        }}
      >
        {t('deviceNew')}
      </Button>
      {draft && (
        <form className="appearance-form" onSubmit={save}>
          <div className="appearance-grid">
            <label className="content-field" htmlFor="device-name">
              <span>{t('deviceName')}</span>
              <input
                id="device-name"
                maxLength={128}
                required
                value={draft.name}
                onChange={(event) =>
                  setDraft({ ...draft, name: event.target.value })
                }
              />
            </label>
            <label className="appearance-check">
              <input
                type="checkbox"
                checked={draft.isActive}
                onChange={(event) =>
                  setDraft({ ...draft, isActive: event.target.checked })
                }
              />
              {t('deviceActiveLabel')}
            </label>
          </div>
          {!isNew && selected && (
            <p>
              {selected.lastSeenAt
                ? `${t('deviceLastSeen')} ${new Date(selected.lastSeenAt).toLocaleString()}`
                : t('deviceNeverSeen')}
              {exampleLink && (
                <>
                  <br />
                  <code>{exampleLink}</code>
                </>
              )}
            </p>
          )}
          <p>{t('deviceOverrideHelp')}</p>
          <div className="appearance-grid">
            <label className="content-field" htmlFor="device-mode">
              <span>{t('themeMode')}</span>
              <select
                id="device-mode"
                value={draft.override.mode ?? ''}
                onChange={(event) =>
                  setOverride({ mode: event.target.value || null })
                }
              >
                <option value="">{t('deviceInherit')}</option>
                <option value="system">{t('modeSystem')}</option>
                <option value="light">{t('modeLight')}</option>
                <option value="dark">{t('modeDark')}</option>
              </select>
            </label>
            <label className="content-field" htmlFor="device-light">
              <span>{t('themeLight')}</span>
              <select
                id="device-light"
                value={draft.override.lightThemeId ?? ''}
                onChange={(event) =>
                  setOverride({ lightThemeId: event.target.value || null })
                }
              >
                {themeOptions('light')}
              </select>
            </label>
            <label className="content-field" htmlFor="device-dark">
              <span>{t('themeDark')}</span>
              <select
                id="device-dark"
                value={draft.override.darkThemeId ?? ''}
                onChange={(event) =>
                  setOverride({ darkThemeId: event.target.value || null })
                }
              >
                {themeOptions('dark')}
              </select>
            </label>
            {numericFields.map((field) => (
              <label
                key={field.key}
                className="content-field"
                htmlFor={`device-${field.key}`}
              >
                <span>{t(field.label)}</span>
                <input
                  id={`device-${field.key}`}
                  type="number"
                  step={field.step}
                  placeholder={t('deviceInherit')}
                  value={draft.override[field.key] ?? ''}
                  onChange={(event) =>
                    setOverride({
                      [field.key]:
                        event.target.value === ''
                          ? null
                          : Number(event.target.value),
                    })
                  }
                />
              </label>
            ))}
          </div>
          <label className="content-field" htmlFor="device-css">
            <span>{t('deviceCss')}</span>
            <textarea
              id="device-css"
              rows={6}
              maxLength={20000}
              spellCheck={false}
              value={draft.customCss}
              onChange={(event) =>
                setDraft({ ...draft, customCss: event.target.value })
              }
            />
          </label>
          <p>{t('deviceCssHelp')}</p>
          <div className="content-editor-tools">
            <Button type="submit" disabled={busy}>
              {t('deviceSave')}
            </Button>
            {!isNew && selected && (
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => void remove()}
              >
                {t('deviceDelete')}
              </Button>
            )}
          </div>
        </form>
      )}
    </Card>
  );
}
