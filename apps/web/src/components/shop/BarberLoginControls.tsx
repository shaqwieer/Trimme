'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { TemporaryPasswordDialog } from '@/components/shop/TemporaryPasswordDialog';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';

export type BarberLogin = components['schemas']['BarberLoginResponse'];

/**
 * A barber's login (D-145), for the shop owner (`/shop/...`) or the platform (`/admin/...`): its state, turning it on,
 * a new temporary password (also how a forgotten password is replaced: barbers have no email), and turning it off,
 * which signs the barber out everywhere. The server generates every temporary password and answers it once (D-146);
 * it is shown in {@link TemporaryPasswordDialog} for the owner to hand over. The barber signs in with the WhatsApp
 * number on their profile.
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
  const [renewOpen, setRenewOpen] = useState(false);
  const [disableOpen, setDisableOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [done, setDone] = useState<string>();
  const [password, setPassword] = useState<string>();
  const path = { params: { path: { professionalId } } };

  const run = async (action: () => Promise<void>) => {
    setPending(true);
    setError(undefined);
    setDone(undefined);
    try {
      await action();
      setRenewOpen(false);
      setDisableOpen(false);
      router.refresh();
    } catch (failure) {
      setRenewOpen(false);
      setDisableOpen(false);
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setPending(false);
    }
  };

  const enable = () =>
    run(async () => {
      const issued = ensureOk(
        scope === 'shop'
          ? await browserApi.PUT('/api/v1/shop/professionals/{professionalId}/login', path)
          : await browserApi.PUT('/api/v1/admin/professionals/{professionalId}/login', path),
      );
      setPassword(issued.temporaryPassword);
      setDone(t('saved', { name }));
    });
  const disable = () =>
    run(async () => {
      ensureOk(
        scope === 'shop'
          ? await browserApi.DELETE('/api/v1/shop/professionals/{professionalId}/login', path)
          : await browserApi.DELETE('/api/v1/admin/professionals/{professionalId}/login', path),
      );
      setDone(t('disabled', { name }));
    });

  const state = !login.enabled
    ? { tone: 'neutral' as const, label: t('state.off') }
    : login.mustChangePassword
      ? { tone: 'warning' as const, label: t('state.temporary') }
      : { tone: 'success' as const, label: t('state.on') };

  return (
    <div className="flex flex-col gap-3" data-testid={`barber-login-${professionalId}`}>
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={state.tone}>{state.label}</Badge>
        {!login.hasNumber && <span className="text-helper text-text-secondary">{t('noNumber')}</span>}
      </div>
      <div className="flex flex-wrap gap-2">
        {login.enabled ? (
          <Button size="sm" variant="outline" disabled={pending} onClick={() => setRenewOpen(true)}>
            {t('newPassword')}
          </Button>
        ) : (
          <Button size="sm" loading={pending} disabled={!login.hasNumber} onClick={() => void enable()}>
            {t('enable')}
          </Button>
        )}
        {login.enabled && (
          <Button size="sm" variant="danger" disabled={pending} onClick={() => setDisableOpen(true)}>
            {t('disable')}
          </Button>
        )}
      </div>
      {done && <InlineAlert tone="success" title={done} />}
      {error && <InlineAlert tone="danger" title={error} />}

      <ConfirmDialog
        open={renewOpen}
        onOpenChange={setRenewOpen}
        tone="default"
        title={t('newPasswordTitle', { name })}
        body={t('newPasswordBody')}
        confirmLabel={t('newPassword')}
        cancelLabel={t('cancel')}
        loading={pending}
        onConfirm={() => void enable()}
      />
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
      <TemporaryPasswordDialog name={name} password={password} onClose={() => setPassword(undefined)} />
    </div>
  );
}
