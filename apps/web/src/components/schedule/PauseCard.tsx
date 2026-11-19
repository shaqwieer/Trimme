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
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import { zonedMidnight } from '@/lib/shop/board';
import { useScheduleErrors } from './errors';

export type OnlineBookingState = components['schemas']['OnlineBookingStateResponse'];

/** Whose pause this is: the owner's own shop, or a shop on the admin's page (D-155). */
export type PauseScope = { kind: 'shop' } | { kind: 'admin'; shopId: string };

/** A `datetime-local` value (YYYY-MM-DDTHH:mm) read on the shop's clock, as an instant. */
function shopInstant(value: string, timeZone: string): string {
  const [date, time] = value.split('T') as [string, string];
  const [hours, minutes] = time.split(':').map(Number) as [number, number];
  return new Date(zonedMidnight(date, timeZone) + (hours * 60 + minutes) * 60_000).toISOString();
}

/**
 * s-hours pause card (2506–2529): live (white) or paused (amber). D-155 (the client's 1-1): the owner's and the admin's,
 * never a barber's. Pausing asks for a reason customers see and an optional return time, when booking opens again by
 * itself. The shop stays listed with «متوقف مؤقتاً»; confirmed appointments are never cancelled — the shop is told how
 * many fall in the pause and handles them from its day screen.
 */
export function PauseCard({
  state,
  timeZone,
  canPause,
  scope = { kind: 'shop' },
}: {
  state: OnlineBookingState;
  timeZone: string;
  canPause: boolean;
  scope?: PauseScope;
}) {
  const t = useTranslations('shopSchedule.pause');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const [done, setDone] = useState<string>();
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState('');
  const [resumesAt, setResumesAt] = useState('');
  const [pending, setPending] = useState(false);
  const { failure, fail, clear } = useScheduleErrors();
  const { paused, pausedAt } = state;
  const when = (instant: string) => ({
    date: formatDate(instant, locale, { timeZone }),
    time: formatTime(instant, locale, timeZone),
  });

  const change = async (pause: boolean) => {
    clear();
    const until = resumesAt ? shopInstant(resumesAt, timeZone) : null;
    if (pause && until && Date.parse(until) <= Date.now()) {
      fail(t('resumesInPast'));
      return;
    }
    setPending(true);
    try {
      const body = { reason: reason.trim() || null, resumesAt: until };
      if (scope.kind === 'admin') {
        const path = { params: { path: { shopId: scope.shopId } } };
        ensureOk(
          pause
            ? await browserApi.POST('/api/v1/admin/shops/{shopId}/online-booking/pause', { ...path, body })
            : await browserApi.POST('/api/v1/admin/shops/{shopId}/online-booking/resume', path),
        );
      } else {
        ensureOk(
          pause
            ? await browserApi.POST('/api/v1/shop/online-booking/pause', { body })
            : await browserApi.POST('/api/v1/shop/online-booking/resume'),
        );
      }
      setConfirming(false);
      setReason('');
      setResumesAt('');
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
        'flex scroll-mt-24 flex-col gap-3 rounded-card border p-5 shadow-e1',
        paused ? 'border-warning-border bg-warning-50' : 'border-border bg-surface',
      )}
      id="pause"
      data-testid="pause-card"
      data-paused={paused}
    >
      <div className="flex items-start gap-3">
        <span
          aria-hidden="true"
          className={cn(
            'flex size-10 shrink-0 items-center justify-center rounded-button',
            paused ? 'bg-warning-50 text-warning-700' : 'bg-brand-100 text-brand-700',
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
          {paused && state.reason && (
            <p className="text-caption font-bold text-text-strong">
              {t('reasonShown', { reason: state.reason })}
            </p>
          )}
          {paused && pausedAt && (
            <p className="text-helper text-text-secondary">{t('since', when(pausedAt))}</p>
          )}
          {paused && (
            <p className="text-helper text-text-secondary" data-testid="pause-resumes">
              {state.resumesAt ? t('resumesAt', when(state.resumesAt)) : t('noReturnTime')}
            </p>
          )}
          {paused && state.byAdmin && scope.kind === 'shop' && (
            <p className="text-helper font-bold text-text-secondary">{t('byAdmin')}</p>
          )}
        </div>
      </div>
      {failure && !confirming && <InlineAlert tone="danger" title={failure} />}
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
          helper={t('reasonHelper')}
          optional
          maxLength={300}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        <TextField
          label={t('resumesAtLabel')}
          helper={t('resumesAtHelper')}
          type="datetime-local"
          optional
          dir="ltr"
          value={resumesAt}
          onChange={(e) => setResumesAt(e.target.value)}
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
