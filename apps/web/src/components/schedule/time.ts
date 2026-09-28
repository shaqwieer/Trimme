import type { components } from '@/lib/api/schema';
import { type AppLocale, formatTime } from '@/lib/i18n/format';

export type Weekday = components['schemas']['DayOfWeek'];
export type HoursInterval = components['schemas']['HoursIntervalDto'];

/** The week as the design shows it: Sunday first (s-hours 4333–4343). */
export const WEEK: readonly Weekday[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];

export const MINUTES_PER_DAY = 1440;

/** "09:05" → 545; null when it is not a time on the 5-minute grid. */
export function toMinutes(value: string): number | null {
  const match = /^(\d{2}):(\d{2})$/.exec(value);
  if (!match) return null;
  const minutes = Number(match[1]) * 60 + Number(match[2]);
  return minutes < MINUTES_PER_DAY && Number(match[2]) < 60 && minutes % 5 === 0 ? minutes : null;
}

/** 545 → "09:05"; minutes past midnight of the next day wrap (1500 → "01:00"). */
export function toClock(minutes: number): string {
  const inDay = ((minutes % MINUTES_PER_DAY) + MINUTES_PER_DAY) % MINUTES_PER_DAY;
  return `${String(Math.floor(inDay / 60)).padStart(2, '0')}:${String(inDay % 60).padStart(2, '0')}`;
}

/** A localized clock time for minutes from midnight ("٩:٠٠ ص" / "9:00 am"), the design's numeral rule (D-040). */
export function formatMinutes(minutes: number, locale: AppLocale): string {
  return formatTime(new Date(Date.UTC(2026, 0, 4, 0, minutes % MINUTES_PER_DAY)), locale, 'UTC');
}

/**
 * One editor row → an API interval. An end at or before the start closes after midnight (09:00 → 00:00 is 09:00 to
 * midnight; 21:00 → 02:00 runs into the next day). Null when a time is not on the 5-minute grid.
 */
export function toInterval(day: Weekday, start: string, end: string): HoursInterval | null {
  const from = toMinutes(start);
  const to = toMinutes(end);
  if (from === null || to === null) return null;
  return { day, startMinute: from, endMinute: to <= from ? to + MINUTES_PER_DAY : to };
}

export type DayRows = { open: boolean; rows: { start: string; end: string }[] };
export type WeekRows = Record<Weekday, DayRows>;

/** Editor state from API intervals; a closed day keeps a sensible default row to reopen with. */
export function toWeekRows(intervals: readonly HoursInterval[]): WeekRows {
  const week = {} as WeekRows;
  for (const day of WEEK) {
    const own = intervals
      .filter((i) => i.day === day)
      .sort((a, b) => a.startMinute - b.startMinute)
      .map((i) => ({ start: toClock(i.startMinute), end: toClock(i.endMinute) }));
    week[day] =
      own.length > 0 ? { open: true, rows: own } : { open: false, rows: [{ start: '09:00', end: '21:00' }] };
  }
  return week;
}

/** API intervals from the editor, or the first invalid day. */
export function fromWeekRows(week: WeekRows): { intervals: HoursInterval[] } | { invalidDay: Weekday } {
  const intervals: HoursInterval[] = [];
  for (const day of WEEK) {
    if (!week[day].open) continue;
    for (const row of week[day].rows) {
      const interval = toInterval(day, row.start, row.end);
      if (!interval) return { invalidDay: day };
      intervals.push(interval);
    }
  }
  return { intervals };
}

/** "9:00 ص – 12:00 ص" for one interval, with a next-day mark when it passes midnight. */
export function intervalLabel(interval: Pick<HoursInterval, 'startMinute' | 'endMinute'>, locale: AppLocale) {
  return `${formatMinutes(interval.startMinute, locale)} – ${formatMinutes(interval.endMinute, locale)}`;
}
