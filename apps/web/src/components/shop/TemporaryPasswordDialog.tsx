'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/overlays';

/**
 * The temporary password the server generated for a barber's login (D-146), shown once: the owner (or admin) copies it
 * or reads it out to the barber, who signs in with their WhatsApp number and this password, then chooses their own.
 * It lives only in the caller's state while the dialog is open; it is never put in a URL, stored or sent anywhere.
 * A shop owner's (D-158) says how an owner signs in instead.
 */
export function TemporaryPasswordDialog({
  name,
  password,
  onClose,
  body,
  once,
}: {
  name: string;
  /** The password to show; the dialog is open while it is set. */
  password: string | undefined;
  onClose: () => void;
  /** Who it is for and how they sign in; the barber's wording by default. */
  body?: string;
  /** What to do if it is lost; the barber's wording by default. */
  once?: string;
}) {
  const t = useTranslations('barberLogins.issued');
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    if (!password) return;
    try {
      await navigator.clipboard.writeText(password);
      setCopied(true);
    } catch {
      // No clipboard (an old browser or a denied permission): the password stays on screen to be read out.
      setCopied(false);
    }
  };

  return (
    <Dialog
      open={password !== undefined}
      onOpenChange={(open) => {
        if (!open) {
          setCopied(false);
          onClose();
        }
      }}
      size="sm"
      title={t('title', { name })}
      description={body ?? t('body')}
      footer={
        <Button
          className="flex-1"
          onClick={() => {
            setCopied(false);
            onClose();
          }}
        >
          {t('done')}
        </Button>
      }
    >
      <div className="flex flex-col gap-3" data-testid="temporary-password">
        <div className="flex flex-wrap items-center gap-3 rounded-field border border-border bg-bg-subtle px-4 py-3">
          <code
            dir="ltr"
            translate="no"
            className="flex-1 font-latin text-[1.375rem] font-bold tracking-[0.12em] break-all text-navy-900 select-all"
            data-testid="temporary-password-value"
          >
            {password}
          </code>
          <Button size="sm" variant="outline" onClick={() => void copy()} aria-live="polite">
            {copied ? t('copied') : t('copy')}
          </Button>
        </div>
        <p className="text-helper text-text-secondary">{once ?? t('once')}</p>
      </div>
    </Dialog>
  );
}
