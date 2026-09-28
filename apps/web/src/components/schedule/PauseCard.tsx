'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { TextField } from '@/components/ui/inputs';
import { Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import { useScheduleErrors } from './errors';

/**
 * s-hours pause card (2506–2529): live (white) or paused (amber). Pausing asks for confirmation with an optional note
 * (the design has none; a mistaken tap would hide the shop). Confirmed appointments are never cancelled (D-013).
 */
export function PauseCard({
  paused,
  pausedAt,
  canPause,
}: {
  paused: boolean;
  pausedAt: string | null;
  canPause: boolean;
}) {
  const t = useTranslations('shopSchedule.pause');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const [done, setDone] = useState<string>();
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const { failure, fail, clear } = useScheduleErrors();

  const change = async (pause: boolean) => {
    clear();
    setPending(true);
    try {
      if (pause)
        ensureOk(
          await browserApi.POST('/api/v1/shop/online-booking/pause', {
            body: { reason: reason.trim() || null },
          }),
        );
      else ensureOk(await browserApi.POST('/api/v1/shop/online-booking/resume'));
      setConfirming(false);
      setReason('');
      setDone(pause ? t('pausedToast') : t('resumedToast'));
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <section
      className={cn(
        'flex flex-col gap-3 rounded-card border p-5 shadow-e1',
        paused ? 'border-warning-border bg-warning-50' : 'border-border bg-surface',
      )}
      data-testid="pause-card"
      data-paused={paused}
    >
      <div className="flex items-start gap-3">
        <span
          aria-hidden="true"
          className={cn(
            'flex size-10 shrink-0 items-center justify-center rounded-button',
            paused ? 'bg-warning-100 text-warning-700' : 'bg-brand-100 text-brand-700',
          )}
        >
          <Icon name="pause" className="size-5" />
        </span>
        <div className="flex flex-col gap-1">
          <h2 className={cn('text-h3 font-bold', paused ? 'text-warning-700' : 'text-text-primary')}>
            {paused ? t('pausedTitle') : t('liveTitle')}
          </h2>
          <p className="text-caption leading-[1.8] text-text-secondary">
            {paused ? t('pausedBody') : t('liveBody')}
          </p>
          {paused && pausedAt && (
            <p className="text-helper text-text-secondary">
              {t('since', { date: formatDate(pausedAt, locale), time: formatTime(pausedAt, locale) })}
            </p>
          )}
        </div>
      </div>
      {failure && <InlineAlert tone="danger" title={failure} />}
      {done && <InlineAlert tone={paused ? 'info' : 'success'} title={done} />}
      {canPause &&
        (paused ? (
          <Button className="self-start" loading={pending} onClick={() => void change(false)}>
            {t('resume')}
          </Button>
        ) : (
          <Button className="self-start" variant="danger" icon="pause" onClick={() => setConfirming(true)}>
            {t('pause')}
          </Button>
        ))}
      <Dialog
        open={confirming}
        onOpenChange={(open) => {
          setConfirming(open);
          if (!open) clear();
        }}
        title={t('confirmTitle')}
        description={t('confirmBody')}
      >
        <TextField
          label={t('reason')}
          optional
          maxLength={300}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        {failure && <InlineAlert tone="danger" title={failure} />}
        <div className="flex flex-wrap gap-2.5">
          <Button variant="dangerSolid" loading={pending} onClick={() => void change(true)}>
            {t('confirm')}
          </Button>
          <Button variant="ghost" onClick={() => setConfirming(false)}>
            {t('cancel')}
          </Button>
        </div>
      </Dialog>
    </section>
  );
}
