import type { components } from '@/lib/api/schema';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export type Notice = components['schemas']['NotificationResponse'];

/**
 * Whose inbox a notice is in: the shop's (the owner's), a customer's own, an admin's own (D-112), or a barber's own
 * login (D-145), which reads through `/me` but links into the shop dashboard.
 */
export type NoticeAudience = 'shop' | 'customer' | 'admin' | 'barber';

const BOOKING_KINDS = [
  'booking.created',
  'booking.pending',
  'booking.confirmed',
  'booking.rescheduled',
  'booking.cancelled',
  'booking.professional_changed',
] as const;

/** Booking changes the owner's inbox words as a barber's when a shop user made them (D-145). */
const BY_BARBER: readonly string[] = ['booking.confirmed', 'booking.rescheduled', 'booking.cancelled'];

/** The kinds each inbox has a message for (the API may add kinds later; unknown ones fall back to a generic line). */
const KNOWN: Record<NoticeAudience, readonly string[]> = {
  shop: [
    'booking.created',
    'booking.pending',
    'booking.confirmed',
    'booking.rescheduled',
    'booking.cancelled',
    'booking.professional_changed',
    'subscription.expiring',
    'subscription.expired',
    'admin.message',
  ],
  customer: ['booking.confirmed', 'booking.rescheduled', 'booking.cancelled', 'booking.professional_changed'],
  barber: BOOKING_KINDS,
  admin: [
    'whatsapp.dispatch_failed',
    'subscription.expiring',
    'subscription.expired',
    'outbox.dead_lettered',
  ],
};

/**
 * The message key under `notifications.kinds` for a notice, or `generic` for a kind this inbox does not know.
 * next-intl nests on dots, so `booking.created` becomes `booking_created`. In the owner's inbox a booking change made by
 * a shop user is a barber's (the owner's own never reach it, D-145), worded as such.
 */
export function noticeKey(audience: NoticeAudience, kind: string, actor?: string): string {
  if (!KNOWN[audience].includes(kind)) return 'generic';
  const key = `${audience}.${kind.replace(/\./g, '_')}`;
  return audience === 'shop' && actor === 'ShopUser' && BY_BARBER.includes(kind) ? `${key}_by_barber` : key;
}

/**
 * The values a notice's message uses, formatted for the reader: names in their language (Arabic as the fallback), the
 * booking's date and time on the operating calendar (D-040), counts as numbers. The API never sends a phone number.
 */
export function noticeValues(notice: Notice, locale: AppLocale): Record<string, string | number> {
  const p = notice.parameters as Record<string, string | undefined>;
  const at = p.startsAt;
  const previous = p.previousStartsAt;
  return {
    customer: p.customerName ?? '',
    item: localizedName(locale, p.itemNameAr ?? '', p.itemNameEn),
    professional: localizedName(locale, p.professionalNameAr ?? '', p.professionalNameEn),
    previousProfessional: localizedName(
      locale,
      p.previousProfessionalNameAr ?? '',
      p.previousProfessionalNameEn,
    ),
    shop: localizedName(locale, p.shopNameAr ?? '', p.shopNameEn),
    date: at ? formatDate(at, locale) : '',
    time: at ? formatTime(at, locale) : '',
    previousDate: previous ? formatDate(previous, locale) : '',
    previousTime: previous ? formatTime(previous, locale) : '',
    reference: p.reference ?? '',
    days: Number(p.daysLeft ?? 0),
    endDate: p.endDate
      ? formatDate(`${p.endDate}T12:00:00+03:00`, locale, { withYear: true, withWeekday: false })
      : '',
    event: p.event ?? '',
    audience: p.audience ?? '',
    message: p.message ?? '',
  };
}

/** Where a notice leads in the reader's area (the booking, the subscription, the failed message), if anywhere. */
export function noticeHref(audience: NoticeAudience, notice: Notice): string | undefined {
  const p = notice.parameters as Record<string, string | undefined>;
  switch (audience) {
    case 'shop':
      if (notice.kind.startsWith('subscription.')) return '/shop/subscription';
      return notice.bookingId ? `/shop/appointments?booking=${notice.bookingId}` : undefined;
    case 'barber':
      return notice.bookingId ? `/shop/appointments?booking=${notice.bookingId}` : undefined;
    case 'customer':
      return notice.bookingId ? `/account/bookings/${notice.bookingId}` : undefined;
    case 'admin':
      if (notice.kind === 'whatsapp.dispatch_failed') return '/admin/whatsapp/dispatches?status=Failed';
      if (notice.kind.startsWith('subscription.') && p.shopId)
        return `/admin/shops/${p.shopId}?tab=subscription`;
      return notice.bookingId ? `/admin/bookings/${notice.bookingId}` : undefined;
  }
}
