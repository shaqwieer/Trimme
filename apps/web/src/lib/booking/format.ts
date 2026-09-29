/** Shared booking display helpers (wizard, confirmation, account pages). Pure, so both server and client use them. */

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
