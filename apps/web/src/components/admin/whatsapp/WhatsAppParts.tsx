'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

/**
 * Retry of a failed dispatch (DV-A13, D-110): confirmed, audited by the API, then the page re-renders with the dispatch
 * queued again. Shown only for failed dispatches to admins with `Admin.WhatsApp.Dispatches.Retry`.
 */
export function RetryDispatchButton({ dispatchId }: { dispatchId: string }) {
  const t = useTranslations('adminWhatsApp.dispatches');
  const apiMessage = useApiErrorMessage();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ tone: 'success' | 'danger'; text: string }>();

  const retry = async () => {
    setBusy(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/whatsapp/dispatches/{dispatchId}/retry', {
          params: { path: { dispatchId } },
        }),
      );
      setResult({ tone: 'success', text: t('retried') });
      router.refresh();
    } catch (error) {
      setResult({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : undefined) ?? t('retry'),
      });
    } finally {
      setBusy(false);
      setOpen(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <div>
        <Button variant="secondary" size="sm" icon="refresh" onClick={() => setOpen(true)} disabled={busy}>
          {t('retry')}
        </Button>
      </div>
      {result && <InlineAlert tone={result.tone} title={result.text} />}
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={t('retryTitle')}
        body={t('retryBody')}
        confirmLabel={t('retry')}
        tone="default"
        loading={busy}
        onConfirm={() => void retry()}
      />
    </div>
  );
}
