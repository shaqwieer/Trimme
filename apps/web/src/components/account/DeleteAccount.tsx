'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, ButtonLink } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

/**
 * Deleting the account (D-144), at the foot of the customer's account page: what it removes, then a confirmation. The
 * API refuses while appointments are ahead, and the answer links to them. Once deleted, the session cookies (and the
 * signed-in hint) are gone, so the home page opens as for any visitor, loaded afresh.
 */
export function DeleteAccount() {
  const t = useTranslations('account.delete');
  const locale = useLocale();
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<{ text: string; upcoming: boolean }>();

  const remove = async () => {
    setPending(true);
    setError(undefined);
    try {
      ensureOk(await browserApi.DELETE('/api/v1/me'));
      // A full load: the router's cached pages were made while signed in (a prefetched home page would still carry
      // the signed-in redirect), and nothing about the deleted account should stay in memory.
      window.location.replace(`/${locale}`);
    } catch (failure) {
      setPending(false);
      setOpen(false);
      const code = failure instanceof ApiError ? failure : 'server.unexpected';
      setError({
        text: apiMessage(code) ?? '',
        upcoming: failure instanceof ApiError && failure.errorCode === 'account.has_upcoming_bookings',
      });
    }
  };

  return (
    <section
      aria-labelledby="account-delete"
      className="flex flex-col gap-3 rounded-card border border-danger-100 bg-surface p-5"
      data-testid="delete-account"
    >
      <h2 id="account-delete" className="text-label font-bold text-danger-700">
        {t('title')}
      </h2>
      <p className="text-helper text-text-secondary">{t('body')}</p>
      {error && (
        <InlineAlert
          tone="danger"
          title={error.text}
          action={
            error.upcoming ? (
              <ButtonLink href="/account/bookings" variant="outline" size="sm">
                {t('upcomingLink')}
              </ButtonLink>
            ) : undefined
          }
        />
      )}
      <Button variant="danger" size="sm" icon="trash" className="self-start" onClick={() => setOpen(true)}>
        {t('button')}
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={t('confirmTitle')}
        body={t('confirmBody')}
        confirmLabel={t('confirm')}
        cancelLabel={t('cancel')}
        onConfirm={remove}
        loading={pending}
      />
    </section>
  );
}
