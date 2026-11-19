import { useLocale, useTranslations } from 'next-intl';
import { InlineAlert } from '@/components/ui/states';
import { changeDeadline, cutoffParts, hasPassed } from '@/lib/booking/format';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';

/**
 * The message after booking (DV-S13): confirmed, since every booking is confirmed at once (D-155, the client's 3-1:
 * no «بانتظار تأكيد المحل»).
 */
export function CreatedBanner() {
  const t = useTranslations('bookings.detail.created');
  return (
    <InlineAlert tone="success" title={t('title')}>
      {t('body')}
    </InlineAlert>
  );
}

/**
 * The cancellation policy of an active booking (D-015, DV-S10). Before the cutoff: until when it can be cancelled or
 * moved online. After it (the API no longer allows either): the window has closed, and TRIMME is the one to contact; no
 * shop phone is shown (D-129). After the shop gave the booking to another barber (D-145): it can be cancelled or changed
 * until it starts, whatever the cutoff. The same after the shop gave it a new time (D-155): the API still allows both
 * past the cutoff, so the page says «until it starts» rather than a deadline already gone.
 */
export function BookingPolicy({
  startsAt,
  cutoffMinutes,
  canChange,
  timeZone,
  movedTo,
}: {
  startsAt: string;
  cutoffMinutes: number;
  canChange: boolean;
  timeZone?: string;
  /** The barber the shop gave the booking to, when it did so since the customer last chose it. */
  movedTo?: string;
}) {
  const t = useTranslations('bookings.detail');
  const locale = useLocale() as AppLocale;
  const deadline = changeDeadline(startsAt, cutoffMinutes);
  const cutoff = cutoffParts(cutoffMinutes);
  return (
    <section aria-labelledby="policy-title" className="flex flex-col gap-2 rounded-card bg-bg-subtle p-4">
      <h2 id="policy-title" className="text-label font-bold text-text-primary">
        {t('policy.title')}
      </h2>
      {canChange && movedTo ? (
        <p className="text-helper leading-[1.8] text-text-secondary" data-testid="moved-policy">
          {t('policy.moved', {
            professional: movedTo,
            time: `${formatDate(startsAt, locale, { timeZone })} ${formatTime(startsAt, locale, timeZone)}`,
          })}
        </p>
      ) : canChange && (cutoffMinutes === 0 || hasPassed(deadline)) ? (
        <p className="text-helper leading-[1.8] text-text-secondary">
          {t('policy.untilStart', {
            time: `${formatDate(startsAt, locale, { timeZone })} ${formatTime(startsAt, locale, timeZone)}`,
          })}
        </p>
      ) : canChange ? (
        <p className="text-helper leading-[1.8] text-text-secondary">
          {t('policy.before', {
            time: `${formatDate(deadline, locale, { timeZone })} ${formatTime(deadline, locale, timeZone)}`,
          })}
        </p>
      ) : (
        <p className="text-helper leading-[1.8] text-text-secondary" data-testid="cutoff-passed">
          {cutoffMinutes === 0
            ? t('policy.started')
            : t('policy.after', { window: t(`window.${cutoff.unit}`, { count: cutoff.count }) })}
        </p>
      )}
    </section>
  );
}
