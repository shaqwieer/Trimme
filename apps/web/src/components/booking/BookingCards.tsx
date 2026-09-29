import { useLocale, useTranslations } from 'next-intl';
import { StatusBadge } from '@/components/ui/Badge';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { bookHref } from '@/lib/booking/links';
import {
  type AppLocale,
  formatDayNumber,
  formatMonthShort,
  formatRating,
  formatTime,
} from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export type CustomerBooking = components['schemas']['CustomerBookingResponse'];

const ACTIVE = new Set(['Pending', 'Confirmed', 'Arrived']);

export function isActive(booking: Pick<CustomerBooking, 'status'>): boolean {
  return ACTIVE.has(booking.status);
}

/** The wizard pre-filled from a booking: same shop, service or package, and professional (c-appointments rebook). */
export function rebookHref(booking: CustomerBooking): string {
  return bookHref(booking.shop.slug, {
    service: booking.item.serviceId ?? undefined,
    package: booking.item.packageId ?? undefined,
    pro: booking.professional.id,
  });
}

const actionClass =
  'inline-flex min-h-11 items-center gap-1.5 rounded-field px-3 text-label font-bold text-brand-700 hover:bg-brand-50';

/**
 * An appointment in "my appointments" (c-appointments 1681–1732): the date block, the service and status, shop and
 * professional, the time, and the one or two actions that apply (details, reschedule, rate, rebook).
 */
export function BookingListItem({ booking }: { booking: CustomerBooking }) {
  const t = useTranslations('bookings');
  const locale = useLocale() as AppLocale;
  const upcoming = isActive(booking);
  const actions = new Set(booking.allowedActions);
  const detail = `/account/bookings/${booking.id}`;
  const service = localizedName(locale, booking.item.nameAr, booking.item.nameEn);

  return (
    <li>
      <article
        className="flex flex-col rounded-card border border-border bg-surface shadow-e1"
        data-testid="booking-card"
      >
        <div className="flex items-center gap-3.5 px-4 py-3.5">
          <div
            className={cn(
              'flex w-[52px] shrink-0 flex-col items-center rounded-field py-2',
              upcoming ? 'bg-brand-100' : 'bg-bg-subtle',
            )}
          >
            <span className="font-latin text-[1.0625rem] font-bold text-navy-900">
              {formatDayNumber(booking.startsAt, locale)}
            </span>
            <span
              className={cn(
                'text-[0.6875rem] font-bold',
                upcoming ? 'text-brand-700' : 'text-text-secondary',
              )}
            >
              {formatMonthShort(booking.startsAt, locale)}
            </span>
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="text-[0.90625rem] font-bold text-text-primary">
                <Link href={detail} className="hover:underline">
                  {service}
                </Link>
              </h3>
              <StatusBadge kind="booking" status={booking.status} size="sm" />
            </div>
            <p className="truncate text-helper text-text-secondary">
              {localizedName(locale, booking.shop.nameAr, booking.shop.nameEn)} ·{' '}
              {localizedName(locale, booking.professional.nameAr, booking.professional.nameEn)}
            </p>
            <p className="flex items-center gap-1.5 text-helper font-bold text-text-strong">
              <Icon name="clock" className="size-3.5 text-text-tertiary" />
              <span>
                {formatTime(booking.startsAt, locale)} — {formatTime(booking.endsAt, locale)}
              </span>
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-1 border-t border-border-row px-2 py-1">
          <Link href={detail} className={actionClass} aria-label={t('actions.detailsLabel', { service })}>
            {t('actions.details')}
          </Link>
          {actions.has('Reschedule') && (
            <Link href={`${detail}/reschedule`} className={actionClass}>
              {t('actions.reschedule')}
            </Link>
          )}
          {actions.has('Review') && (
            <Link href={`${detail}/review`} className={actionClass}>
              <Icon name="star" className="size-4" />
              {t('actions.review')}
            </Link>
          )}
          {booking.reviewRating != null && (
            <span className="inline-flex min-h-11 items-center gap-1 px-3 text-label font-bold text-success-700">
              {t('actions.rated', { rating: formatRating(booking.reviewRating, locale) })}
            </span>
          )}
          {!upcoming && (booking.status !== 'Completed' || booking.reviewRating != null) && (
            <Link href={rebookHref(booking)} className={actionClass}>
              <Icon name="refresh" className="size-4" />
              {t('actions.rebook')}
            </Link>
          )}
        </div>
      </article>
    </li>
  );
}

/** "Book your next appointment" (c-appointments 1706–1710): same shop and barber, two taps. */
export function RebookCard({ booking }: { booking: CustomerBooking }) {
  const t = useTranslations('bookings.bookNext');
  return (
    <Link
      href={rebookHref(booking)}
      className="flex min-h-16 items-center gap-3 rounded-card border border-dashed border-border-strong px-4 py-3 hover:bg-bg-subtle"
    >
      <span className="flex size-10 items-center justify-center rounded-full bg-brand-100 text-brand-700">
        <Icon name="plus" className="size-5" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-label font-bold text-text-primary">{t('title')}</span>
        <span className="block text-helper text-text-secondary">{t('body')}</span>
      </span>
      <Icon name="chevL" className="size-4 text-text-tertiary" />
    </Link>
  );
}
