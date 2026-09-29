import { type AppLocale, formatTime } from '@/lib/i18n/format';

/** A time of day from minutes after midnight ("٩:٠٠ ص"); minutes past 24:00 wrap to the next day (D-082). */
export function minuteOfDay(minute: number, locale: AppLocale): string {
  const wrapped = ((minute % 1440) + 1440) % 1440;
  return formatTime(new Date(Date.UTC(2000, 0, 1, Math.floor(wrapped / 60), wrapped % 60)), locale, 'UTC');
}

const RELATIVE_LOCALE: Record<AppLocale, string> = { ar: 'ar-SA-u-nu-arab', en: 'en' };

/** "قبل ٣ أيام" / "3 days ago" (clock-style digits in Arabic, D-040). */
export function relativeTime(instant: string, locale: AppLocale, now: Date = new Date()): string {
  const seconds = Math.round((new Date(instant).getTime() - now.getTime()) / 1000);
  const format = new Intl.RelativeTimeFormat(RELATIVE_LOCALE[locale], { numeric: 'auto' });
  const units: Array<[Intl.RelativeTimeFormatUnit, number]> = [
    ['year', 365 * 24 * 3600],
    ['month', 30 * 24 * 3600],
    ['week', 7 * 24 * 3600],
    ['day', 24 * 3600],
    ['hour', 3600],
    ['minute', 60],
  ];
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return format.format(Math.round(seconds / size), unit);
  }
  return format.format(0, 'minute');
}

/** Splits a duration in minutes into whole hours when it is one (120 → 2 hours), for policy text. */
export function durationParts(minutes: number): { unit: 'hours' | 'minutes'; count: number } {
  return minutes % 60 === 0 ? { unit: 'hours', count: minutes / 60 } : { unit: 'minutes', count: minutes };
}
