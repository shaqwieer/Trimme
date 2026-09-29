import { useLocale, useTranslations } from 'next-intl';
import { Ltr } from '@/components/text/Ltr';
import { Icon } from '@/components/ui/icons';
import { InlineAlert } from '@/components/ui/states';
import { changeDeadline, cutoffParts } from '@/lib/booking/format';
import { type AppLocale, formatDate, formatPhone, formatTime } from '@/lib/i18n/format';

/**
 * The message after booking (DV-S13): "confirmed" only when the shop auto-confirms; a shop that confirms manually
 * leaves the booking Pending, so the customer is told the request was sent.
 */
export function CreatedBanner({ status }: { status: string }) {
  const t = useTranslations('bookings.detail.created');
  const copy = status === 'Pending' ? 'pending' : 'confirmed';
  return (
    <InlineAlert tone="success" title={t(`${copy}.title`)}>
      {t(`${copy}.body`)}
    </InlineAlert>
  );
}

/**
 * The cancellation policy of an active booking (D-015, DV-S10). Before the cutoff: until when it can be cancelled or
 * moved online. After it (the API no longer allows either): the window has closed, and the shop's phone to call.
 */
export function BookingPolicy({
  startsAt,
  cutoffMinutes,
  canChange,
  shopPhone,
  timeZone,
}: {
  startsAt: string;
  cutoffMinutes: number;
  canChange: boolean;
  shopPhone: string | null;
  timeZone?: string;
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
      {canChange ? (
        <p className="text-helper leading-[1.8] text-text-secondary">
          {t('policy.before', {
            time: `${formatDate(deadline, locale, { timeZone })} ${formatTime(deadline, locale, timeZone)}`,
          })}
        </p>
      ) : (
        <>
          <p className="text-helper leading-[1.8] text-text-secondary" data-testid="cutoff-passed">
            {t('policy.after', { window: t(`window.${cutoff.unit}`, { count: cutoff.count }) })}
          </p>
          {shopPhone && (
            <a
              href={`tel:${shopPhone}`}
              className="inline-flex min-h-11 items-center gap-2 self-start rounded-button border border-border-input bg-surface px-4 text-label font-bold text-text-strong"
            >
              <Icon name="phone" className="size-4" />
              {t('policy.callShop')} <Ltr>{formatPhone(shopPhone)}</Ltr>
            </a>
          )}
        </>
      )}
    </section>
  );
}
