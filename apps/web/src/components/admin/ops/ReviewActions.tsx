'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { TextareaField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';

export const MIN_MODERATION_REASON = 5;

type Action = 'flag' | 'hide' | 'publish';

/**
 * Moderation of one review (a-reviews, D-102): staff report it with a reason (`Admin.Reviews.Flag`); moderators hide it
 * with a reason or publish it again, which also clears a report (`Admin.Reviews.Moderate`). Only the actions that fit
 * the review's state are offered; the API enforces the same rules, audits each action and moves the rating totals.
 */
export function ReviewActions({
  reviewId,
  version,
  status,
  reported,
  canFlag,
  canModerate,
}: {
  reviewId: string;
  version: number;
  status: 'Published' | 'Hidden';
  reported: boolean;
  canFlag: boolean;
  canModerate: boolean;
}) {
  const t = useTranslations('adminReviews.actions');
  const tv = useTranslations('validation');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [action, setAction] = useState<Action>();
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  const offered: Action[] = [
    ...(canFlag && status === 'Published' && !reported ? (['flag'] as const) : []),
    ...(canModerate && status === 'Published' ? (['hide'] as const) : []),
    ...(canModerate && (status === 'Hidden' || reported) ? (['publish'] as const) : []),
  ];

  const run = async () => {
    if (action !== 'publish' && reason.trim().length < MIN_MODERATION_REASON) {
      setReasonError(tv('reasonRequired'));
      return;
    }
    setReasonError(undefined);
    setBusy(true);
    try {
      const path = { params: { path: { reviewId } } };
      if (action === 'flag') {
        ensureOk(
          await browserApi.POST('/api/v1/admin/reviews/{reviewId}/flag', {
            ...path,
            body: { reason: reason.trim(), version },
          }),
        );
      } else if (action === 'hide') {
        ensureOk(
          await browserApi.POST('/api/v1/admin/reviews/{reviewId}/hide', {
            ...path,
            body: { reason: reason.trim(), version },
          }),
        );
      } else {
        ensureOk(
          await browserApi.POST('/api/v1/admin/reviews/{reviewId}/publish', { ...path, body: { version } }),
        );
      }
      setAction(undefined);
      setFailure(undefined);
      router.refresh();
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      setAction(undefined);
      if (error instanceof ApiError && error.status === 409) router.refresh();
    } finally {
      setBusy(false);
    }
  };

  if (offered.length === 0) return null;
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap gap-2">
        {offered.map((a) => (
          <Button
            key={a}
            size="sm"
            variant={a === 'hide' ? 'danger' : a === 'publish' ? 'secondary' : 'outline'}
            onClick={() => {
              setAction(a);
              setReason('');
              setReasonError(undefined);
            }}
          >
            {t(a)}
          </Button>
        ))}
      </div>
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Dialog
        open={action === 'flag' || action === 'hide'}
        onOpenChange={(open) => !open && setAction(undefined)}
        title={action === 'hide' ? t('hideTitle') : t('flagTitle')}
        description={action === 'hide' ? t('hideBody') : t('flagBody')}
        footer={
          <Button
            size="md"
            variant={action === 'hide' ? 'dangerSolid' : 'primary'}
            loading={busy}
            onClick={() => void run()}
          >
            {action === 'hide' ? t('hide') : t('flag')}
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
      <ConfirmDialog
        open={action === 'publish'}
        onOpenChange={(open) => !open && setAction(undefined)}
        tone="default"
        title={t('publishTitle')}
        body={status === 'Hidden' ? t('publishBody') : t('clearBody')}
        confirmLabel={t('publish')}
        loading={busy}
        onConfirm={() => void run()}
      />
    </div>
  );
}
