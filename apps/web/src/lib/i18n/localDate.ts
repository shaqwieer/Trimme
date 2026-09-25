import { OPERATING_TIME_ZONE } from './config';
import type { AppLocale } from './format';

/**
 * A calendar day in the shop's time zone, as "YYYY-MM-DD". Booking UIs work with these strings
 * (never Date objects) so a day can never shift when the browser or test runner is in another zone.
 */
export type LocalDate = string;

const TIME_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-arab-ca-gregory',
  en: 'en-GB-u-nu-latn-ca-gregory',
};

export function parseLocalDate(value: LocalDate): { year: number; month: number; day: number } {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) throw new Error(`Invalid local date: ${value}`);
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

/** Noon UTC of the given day: formatting it with timeZone "UTC" always yields the same calendar day. */
export function toUtcNoon(value: LocalDate): Date {
  const { year, month, day } = parseLocalDate(value);
  return new Date(Date.UTC(year, month - 1, day, 12));
}

export function toLocalDate(date: Date): LocalDate {
  return date.toISOString().slice(0, 10);
}

export function addDays(value: LocalDate, days: number): LocalDate {
  const date = toUtcNoon(value);
  date.setUTCDate(date.getUTCDate() + days);
  return toLocalDate(date);
}

/** 0 = Sunday … 6 = Saturday (weeks start on Sunday in the design). */
export function weekday(value: LocalDate): number {
  return toUtcNoon(value).getUTCDay();
}

export function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** "YYYY-MM" → [first day, …, last day]. */
export function monthDays(yearMonth: string): LocalDate[] {
  const [year, month] = yearMonth.split('-').map(Number) as [number, number];
  return Array.from(
    { length: daysInMonth(year, month) },
    (_, i) => `${yearMonth}-${String(i + 1).padStart(2, '0')}`,
  );
}

export function shiftMonth(yearMonth: string, delta: number): string {
  const [year, month] = yearMonth.split('-').map(Number) as [number, number];
  const date = new Date(Date.UTC(year, month - 1 + delta, 1));
  return date.toISOString().slice(0, 7);
}

/** Today's date in the operating time zone. */
export function todayLocal(timeZone = OPERATING_TIME_ZONE, now = new Date()): LocalDate {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
  return parts;
}

/** Weekday name ("الجمعة" / "Fri"), month name, "سبتمبر ٢٠٢٦" — date text follows D-040. */
export function formatLocalDate(
  value: LocalDate,
  locale: AppLocale,
  options: Intl.DateTimeFormatOptions,
): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], { ...options, timeZone: 'UTC' }).format(
    toUtcNoon(value),
  );
}

export function formatMonthYear(yearMonth: string, locale: AppLocale): string {
  return formatLocalDate(`${yearMonth}-01`, locale, { month: 'long', year: 'numeric' });
}

/** Hour (0–23) of an instant in the given zone — used to group slots into periods. */
export function hourInZone(instant: string, timeZone = OPERATING_TIME_ZONE): number {
  const hour = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', hourCycle: 'h23', timeZone }).format(
    new Date(instant),
  );
  return Number(hour);
}
