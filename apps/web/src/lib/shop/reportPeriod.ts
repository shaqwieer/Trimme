import { addDays, daysInMonth, type LocalDate, parseLocalDate, weekday } from '@/lib/i18n/localDate';

/** The owner's report periods (D-147: «باليوم والأسبوع والشهر»). */
export type ReportPeriod = 'day' | 'week' | 'month';

export const REPORT_PERIODS: readonly ReportPeriod[] = ['day', 'week', 'month'];

/**
 * The shop-local dates a period covers around `date`: the day itself; its week, Sunday to Saturday (weeks start on
 * Sunday in the design); or its calendar month. `previous` and `next` are dates in the neighbouring periods.
 */
export function reportRange(
  period: ReportPeriod,
  date: LocalDate,
): { from: LocalDate; to: LocalDate; previous: LocalDate; next: LocalDate } {
  if (period === 'day') return { from: date, to: date, previous: addDays(date, -1), next: addDays(date, 1) };
  if (period === 'week') {
    const from = addDays(date, -weekday(date));
    return { from, to: addDays(from, 6), previous: addDays(from, -7), next: addDays(from, 7) };
  }
  const { year, month } = parseLocalDate(date);
  const from = `${year}-${String(month).padStart(2, '0')}-01`;
  const to = addDays(from, daysInMonth(year, month) - 1);
  return { from, to, previous: addDays(from, -1), next: addDays(to, 1) };
}

export function asReportPeriod(value: string | undefined): ReportPeriod {
  return REPORT_PERIODS.includes(value as ReportPeriod) ? (value as ReportPeriod) : 'day';
}
