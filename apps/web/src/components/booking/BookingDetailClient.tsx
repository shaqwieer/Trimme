'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/overlays';
import { Chip } from '@/components/ui/selection.client';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { calendarDaysUntil, timeUntil } from '@/lib/booking/format';
import type { AppLocale } from '@/lib/i18n/format';

/**
 * "In 2 days" / "بعد يومين" until the appointment (c-appointments 1760: the countdown reduces forgetting). Computed in
 * the browser after mount, so the server and client renders never disagree, and refreshed every minute. From a day
 * away it counts calendar days in the shop's time zone, so "tomorrow" always means the next date.
 */
export function Countdown({ startsAt, timeZone }: { startsAt: string; timeZone?: string }) {
  const locale = useLocale() as AppLocale;
  const [text, setText] = useState<string>();

  useEffect(() => {
    const format = new Intl.RelativeTimeFormat(locale === 'ar' ? 'ar-SA-u-nu-arab' : 'en-GB', {
      numeric: 'auto',
    });
    const update = () => {
      if (new Date(startsAt).getTime() <= Date.now()) {
        setText(undefined);
        return;
      }
      const { days, hours, minutes } = timeUntil(startsAt);
      setText(
        days > 0
          ? format.format(calendarDaysUntil(startsAt, timeZone), 'day')
          : hours > 0
            ? format.format(hours, 'hour')
            : format.format(Math.max(minutes, 1), 'minute'),
      );
    };
    update();
    const timer = setInterval(update, 60_000);
    return () => clearInterval(timer);
  }, [locale, startsAt, timeZone]);

  return (
    <p className="min-h-6 text-label font-bold text-brand-700" data-testid="booking-countdown">
      {text}
    </p>
  );
}

const REASONS = ['emergency', 'timeChanged', 'bookLater'] as const;

/**
 * Cancelling is destructive (c-rate 1809–1841): a confirmation dialog that says the shop is told and the time is
 * released, optional reason chips, "yes, cancel" in the danger colour and "keep" as the safe default. The version read
 * with the page is sent, so a booking the shop changed meanwhile is not cancelled blindly.
 */
export function CancelBookingButton({
  bookingId,
  version,
  when,
}: {
  bookingId: string;
  version: number;
  when: string;
}) {
  const t = useTranslations('bookings.cancel');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState<(typeof REASONS)[number]>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();

  const cancel = async () => {
    setPending(true);
    setError(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/me/bookings/{bookingId}/cancel', {
          params: { path: { bookingId } },
          body: { reason: reason ? t(`reasons.${reason}`) : null, version },
        }),
      );
      setOpen(false);
      router.replace(`/account/bookings/${bookingId}?cancelled=1`);
      router.refresh();
    } catch (failure) {
      setPending(false);
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
      if (failure instanceof ApiError && failure.status === 409) router.refresh();
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setError(undefined);
      }}
      size="sm"
      title={t('title', { when })}
      description={t('body')}
      trigger={
        <Button variant="danger" fullWidth>
          {t('open')}
        </Button>
      }
      footer={
        <>
          <Button variant="dangerSolid" className="flex-1" loading={pending} onClick={cancel}>
            {t('confirm')}
          </Button>
          <Button variant="outline" className="flex-1" autoFocus onClick={() => setOpen(false)}>
            {t('keep')}
          </Button>
        </>
      }
    >
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="pb-1 text-label font-bold text-text-strong">{t('reasonLabel')}</legend>
        <div className="flex flex-wrap gap-2">
          {REASONS.map((key) => (
            <Chip
              key={key}
              pressed={reason === key}
              onPressedChange={(on) => setReason(on ? key : undefined)}
            >
              {t(`reasons.${key}`)}
            </Chip>
          ))}
        </div>
      </fieldset>
      {error && <InlineAlert tone="danger" title={error} />}
    </Dialog>
  );
}
