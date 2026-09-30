/** Shared booking display helpers (wizard, confirmation, account pages). Pure, so both server and client use them. */

import { OPERATING_TIME_ZONE } from '@/lib/i18n/config';

/** The end instant of a booking that starts at `startsAt` and lasts `minutes`. */
export function endOf(startsAt: string, minutes: number): string {
  return new Date(new Date(startsAt).getTime() + minutes * 60_000).toISOString();
}

/** A cancellation cutoff as whole hours when it is one ("2 hours"), otherwise minutes ("90 minutes"). */
export function cutoffParts(minutes: number): { unit: 'hours' | 'minutes'; count: number } {
  return minutes >= 60 && minutes % 60 === 0
    ? { unit: 'hours', count: minutes / 60 }
    : { unit: 'minutes', count: minutes };
}

/** The last moment the customer can cancel or reschedule online (D-015). */
export function changeDeadline(startsAt: string, cutoffMinutes: number): string {
  return new Date(new Date(startsAt).getTime() - cutoffMinutes * 60_000).toISOString();
}

/** Days, hours and minutes until `target` (never negative), for the countdown on the booking page. */
export function timeUntil(
  target: string,
  now: Date = new Date(),
): { days: number; hours: number; minutes: number } {
  const total = Math.max(0, Math.floor((new Date(target).getTime() - now.getTime()) / 60_000));
  return { days: Math.floor(total / 1440), hours: Math.floor((total % 1440) / 60), minutes: total % 60 };
}

/**
 * Calendar days from `now` to `target` in the shop's time zone (never negative). The countdown counts days this way, so
 * a visit the day after tomorrow reads "in 2 days" even when it is less than 48 hours away.
 */
export function calendarDaysUntil(
  target: string,
  timeZone: string = OPERATING_TIME_ZONE,
  now: Date = new Date(),
): number {
  const dayOf = (instant: Date) => {
    const [y, m, d] = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    })
      .format(instant)
      .split('-')
      .map(Number) as [number, number, number];
    return Date.UTC(y, m - 1, d);
  };
  return Math.max(0, Math.round((dayOf(new Date(target)) - dayOf(now)) / 86_400_000));
}

/**
 * Where a booking came from, as the shop and admin lists label it (design «التطبيق / رمز QR / حضوري»): an online booking
 * credited to a QR scan shows «رمز QR» (D-114).
 */
export function bookingSource(booking: {
  channel: 'Online' | 'WalkIn';
  viaQr: boolean;
}): 'Online' | 'WalkIn' | 'Qr' {
  return booking.channel === 'Online' && booking.viaQr ? 'Qr' : booking.channel;
}
