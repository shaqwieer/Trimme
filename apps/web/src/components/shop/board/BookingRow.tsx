import { useLocale } from 'next-intl';
import { StatusBadge } from '@/components/ui/Badge';
import { Link } from '@/i18n/navigation';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDurationMinutes, formatPrice, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export type ShopBooking = components['schemas']['ShopBookingResponse'];

const bar: Record<string, string> = {
  Pending: 'bg-status-pending-dot',
  Confirmed: 'bg-status-confirmed-dot',
  Arrived: 'bg-status-arrived-dot',
  Completed: 'bg-status-completed-dot',
  NoShow: 'bg-status-noshow-dot',
  CancelledByCustomer: 'bg-status-cancelled-dot',
  CancelledByShop: 'bg-status-cancelled-dot',
};

/**
 * A booking in the overview's "today" list (s-overview 2168–2186): time and duration, a status bar, the customer's name,
 * status, item and professional, and the snapshot price. It opens the appointment in the list's drawer. No phone.
 */
export function BookingRow({
  booking,
  timeZone,
  highlight = false,
}: {
  booking: ShopBooking;
  timeZone: string;
  highlight?: boolean;
}) {
  const locale = useLocale() as AppLocale;
  const professional = localizedName(locale, booking.professional.nameAr, booking.professional.nameEn);
  return (
    <Link
      href={`/shop/appointments?booking=${booking.id}`}
      className={cn(
        'flex items-center gap-3 rounded-card border px-3 py-2.5 transition-colors hover:border-brand-500',
        highlight ? 'border-brand-200 bg-brand-50' : 'border-border-row bg-surface',
      )}
    >
      <span className="flex w-16 shrink-0 flex-col">
        <span className="text-label font-bold text-text-primary">
          {formatTime(booking.startsAt, locale, timeZone)}
        </span>
        <span className="text-badge text-text-tertiary">
          {formatDurationMinutes(booking.item.durationMinutes, locale)}
        </span>
      </span>
      <span
        aria-hidden="true"
        className={cn('h-9 w-1 shrink-0 rounded-full', bar[booking.status] ?? 'bg-border')}
      />
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-2">
          <span className="truncate text-label font-bold text-text-primary">{booking.customerName}</span>
          <StatusBadge kind="booking" status={booking.status} size="sm" />
        </span>
        <span className="block truncate text-helper text-text-secondary">
          {localizedName(locale, booking.item.nameAr, booking.item.nameEn)} · {professional.split(' ')[0]}
        </span>
      </span>
      <span className="shrink-0 font-latin text-label font-bold text-navy-900">
        {formatPrice(booking.item.price, locale, booking.item.currency)}
      </span>
    </Link>
  );
}
