import type { components } from '@/lib/api/schema';
import type { AppLocale } from '@/lib/i18n/format';
import { addDays, daysInMonth, formatLocalDate, type LocalDate, parseLocalDate } from '@/lib/i18n/localDate';

type PlanPrice = components['schemas']['PlanPriceResponse'];
export type IntervalUnit = components['schemas']['BillingIntervalUnit'];

const pad = (value: number) => String(value).padStart(2, '0');

/**
 * Last day (inclusive) of a period, mirroring the server's `SubscriptionDates.End` for previews only (the API
 * computes and records the real end): start + interval − 1 day; when the month has no such day (31 Jan + 1 month),
 * the period runs to the end of that month.
 */
export function periodEnd(start: LocalDate, unit: IntervalUnit, count: number): LocalDate {
  if (unit === 'Day') return addDays(start, count - 1);
  const { year, month, day } = parseLocalDate(start);
  const index = year * 12 + (month - 1) + count;
  const targetYear = Math.floor(index / 12);
  const targetMonth = (index % 12) + 1;
  const last = daysInMonth(targetYear, targetMonth);
  if (day > last) return `${targetYear}-${pad(targetMonth)}-${pad(last)}`;
  return addDays(`${targetYear}-${pad(targetMonth)}-${pad(day)}`, -1);
}

/** The price version in force on `date`: the latest that starts on or before it. */
export function priceOn(prices: readonly PlanPrice[], date: LocalDate): PlanPrice | undefined {
  return [...prices]
    .filter((price) => price.effectiveFrom <= date)
    .sort((a, b) => (a.effectiveFrom < b.effectiveFrom ? 1 : -1))[0];
}

/** "٣٠ سبتمبر ٢٠٢٧" (ar) · "30 September 2027" (en). */
export const longDate = (value: LocalDate, locale: AppLocale) =>
  formatLocalDate(value, locale, { day: 'numeric', month: 'long', year: 'numeric' });
