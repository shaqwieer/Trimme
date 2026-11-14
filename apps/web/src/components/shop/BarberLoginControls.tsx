'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { TextField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';

export type BarberLogin = components['schemas']['BarberLoginResponse'];

const MIN_PASSWORD = 10;
const MAX_PASSWORD = 256;

/**
 * A barber's login (D-145), for the shop owner (`/shop/...`) or the platform (`/admin/...`): its state, turning it on
 * with a temporary password (also how a forgotten password is replaced: barbers have no email), and turning it off,
 * which signs the barber out everywhere. The barber signs in with the WhatsApp number on their profile, which only the
 * platform sets.
 */
export function BarberLoginControls({
  scope,
  professionalId,
  name,
  login,
}: {
  scope: 'shop' | 'admin';
  professionalId: string;
  name: string;
  login: BarberLogin;
}) {
  const t = useTranslations('barberLogins');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [passwordOpen, setPasswordOpen] = useState(false);
  const [disableOpen, setDisableOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [done, setDone] = useState<string>();
  const path = { params: { path: { professionalId } } };
  const tooShort = password.length > 0 && password.length < MIN_PASSWORD;

  const run = async (action: () => Promise<unknown>, success: string) => {
    setPending(true);
    setError(undefined);
    setDone(undefined);
    try {
      await action();
      setDone(success);
      setPasswordOpen(false);
      setDisableOpen(false);
      setPassword('');
      router.refresh();
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setPending(false);
    }
  };

  const enable = () =>
    run(
      async () =>
        ensureOk(
          scope === 'shop'
            ? await browserApi.PUT('/api/v1/shop/professionals/{professionalId}/login', {
                ...path,
                body: { password },
              })
            : await browserApi.PUT('/api/v1/admin/professionals/{professionalId}/login', {
                ...path,
                body: { password },
              }),
        ),
      t('saved', { name }),
    );
  const disable = () =>
    run(
      async () =>
        ensureOk(
          scope === 'shop'
            ? await browserApi.DELETE('/api/v1/shop/professionals/{professionalId}/login', path)
            : await browserApi.DELETE('/api/v1/admin/professionals/{professionalId}/login', path),
        ),
      t('disabled', { name }),
    );

  const state = !login.enabled
    ? { tone: 'neutral' as const, label: t('state.off') }
    : login.mustChangePassword
      ? { tone: 'warning' as const, label: t('state.temporary') }
      : { tone: 'success' as const, label: t('state.on') };

  return (
    <div className="flex flex-col gap-3" data-testid={`barber-login-${professionalId}`}>
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={state.tone}>{state.label}</Badge>
        {!login.hasNumber && (
          <span className="text-helper text-text-secondary">
            {scope === 'shop' ? t('noNumberShop') : t('noNumberAdmin')}
          </span>
        )}
      </div>
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          variant={login.enabled ? 'outline' : 'primary'}
          disabled={!login.hasNumber || pending}
          onClick={() => {
            setError(undefined);
            setPasswordOpen(true);
          }}
        >
          {login.enabled ? t('newPassword') : t('enable')}
        </Button>
        {login.enabled && (
          <Button size="sm" variant="danger" disabled={pending} onClick={() => setDisableOpen(true)}>
            {t('disable')}
          </Button>
        )}
      </div>
      {done && <InlineAlert tone="success" title={done} />}
      {error && !passwordOpen && <InlineAlert tone="danger" title={error} />}

      <Dialog
        open={passwordOpen}
        onOpenChange={setPasswordOpen}
        size="sm"
        title={login.enabled ? t('newPasswordTitle', { name }) : t('enableTitle', { name })}
        description={t('passwordBody')}
        footer={
          <>
            <Button
              className="flex-1"
              loading={pending}
              disabled={password.length < MIN_PASSWORD || password.length > MAX_PASSWORD}
              onClick={() => void enable()}
            >
              {t('save')}
            </Button>
            <Button variant="outline" className="flex-1" onClick={() => setPasswordOpen(false)}>
              {t('cancel')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <TextField
            label={t('password')}
            helper={t('passwordHint')}
            error={tooShort ? t('passwordTooShort') : undefined}
            type="text"
            autoComplete="off"
            dir="ltr"
            maxLength={MAX_PASSWORD}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          {error && <InlineAlert tone="danger" title={error} />}
        </div>
      </Dialog>

      <ConfirmDialog
        open={disableOpen}
        onOpenChange={setDisableOpen}
        title={t('disableTitle', { name })}
        body={t('disableBody')}
        confirmLabel={t('disable')}
        cancelLabel={t('cancel')}
        loading={pending}
        onConfirm={() => void disable()}
      />
    </div>
  );
}
