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

export const CONTACT_MESSAGE_MIN = 5;
export const CONTACT_MESSAGE_MAX = 500;

/**
 * «تواصل مع المحل» (a-reviews, D-102, D-112): a moderator writes the review's shop a message, delivered to the shop's
 * notifications. The audit records that a message was sent, not its text.
 */
export function ContactShopButton({ reviewId }: { reviewId: string }) {
  const t = useTranslations('adminReviews.contact');
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);

  const send = async () => {
    const text = message.trim();
    if (text.length < CONTACT_MESSAGE_MIN || text.length > CONTACT_MESSAGE_MAX) {
      setError(t('length'));
      return;
    }
    setError(undefined);
    setBusy(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/reviews/{reviewId}/contact-shop', {
          params: { path: { reviewId } },
          body: { message: text },
        }),
      );
      setSent(true);
      setOpen(false);
      setMessage('');
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : undefined) ?? t('failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <Dialog
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (next) setSent(false);
        }}
        trigger={
          <Button variant="ghost" size="sm" icon="msg">
            {t('open')}
          </Button>
        }
        title={t('title')}
        description={t('body')}
        footer={
          <Button variant="primary" onClick={() => void send()} disabled={busy}>
            {t('send')}
          </Button>
        }
      >
        <TextareaField
          label={t('message')}
          value={message}
          maxLength={CONTACT_MESSAGE_MAX}
          rows={4}
          error={error}
          aria-invalid={error ? true : undefined}
          onChange={(event) => setMessage(event.target.value)}
        />
      </Dialog>
      {sent && <InlineAlert tone="success" title={t('sent')} />}
    </div>
  );
}
