'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { TextareaField } from '@/components/ui/inputs';
import { Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

export const MIN_REVEAL_REASON = 5;

/**
 * The customer's mobile, masked, with «إظهار الرقم» for admins who hold `Admin.Customers.ViewContact` (DV-S17, D-105).
 * The reveal needs a reason, is audited by the API and is never cached; the number lives only in this component's state
 * until the admin hides it or leaves the page.
 */
export function CustomerContact({
  customerId,
  masked,
  canReveal,
}: {
  customerId: string;
  masked: string | null;
  canReveal: boolean;
}) {
  const t = useTranslations('adminCustomers.contact');
  const tv = useTranslations('validation');
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();
  const [revealed, setRevealed] = useState<string>();

  const reveal = async () => {
    if (reason.trim().length < MIN_REVEAL_REASON) {
      setReasonError(tv('reasonRequired'));
      return;
    }
    setReasonError(undefined);
    setBusy(true);
    try {
      const result = ensureOk(
        await browserApi.POST('/api/v1/admin/customers/{customerId}/contact/reveal', {
          params: { path: { customerId } },
          body: { reason: reason.trim() },
        }),
      );
      setRevealed(result.phone);
      setOpen(false);
      setReason('');
      setFailure(undefined);
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      setOpen(false);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-2" data-testid="customer-contact">
      <div className="flex flex-wrap items-center gap-3">
        <span
          dir="ltr"
          className="font-latin text-body font-bold text-text-primary"
          data-testid="customer-phone"
        >
          {revealed ?? masked ?? t('none')}
        </span>
        {canReveal && masked && !revealed && (
          <Button size="sm" variant="outline" icon="eyeOff" onClick={() => setOpen(true)}>
            {t('reveal')}
          </Button>
        )}
        {revealed && (
          <Button size="sm" variant="ghost" onClick={() => setRevealed(undefined)}>
            {t('hide')}
          </Button>
        )}
      </div>
      <p className="text-helper text-text-secondary">{t('privacy')}</p>
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title={t('title')}
        description={t('body')}
        footer={
          <Button size="md" loading={busy} onClick={() => void reveal()}>
            {t('submit')}
          </Button>
        }
      >
        <TextareaField
          label={t('reason')}
          value={reason}
          maxLength={300}
          onChange={(event) => setReason(event.target.value)}
          error={reasonError}
        />
      </Dialog>
    </div>
  );
}
