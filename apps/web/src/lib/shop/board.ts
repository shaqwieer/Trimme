/**
 * Pure helpers of the shop dashboard (overview, calendar, appointments). Times are instants from the API; everything
 * here positions and groups them for display only — the server decides availability and business days (D-100).
 */
import type { AppLocale } from '@/lib/i18n/format';

/* ---------------------------------------------------------------- overview */

/** Booked share of the bookable minutes, 0–100 (a day with no bookable time is 0). */
export function loadPercent(bookedMinutes: number, availableMinutes: number): number {
  if (availableMinutes <= 0) return 0;
  return Math.min(100, Math.round((bookedMinutes / availableMinutes) * 100));
}

/** The hours between the first and last hour with bookings (a default working range when there are none). */
export function trimHours<T extends { hour: number; count: number }>(
  hours: T[],
  fallback: [number, number] = [9, 22],
): T[] {
  const busy = hours.filter((h) => h.count > 0).map((h) => h.hour);
  const [from, to] = busy.length > 0 ? [Math.min(...busy), Math.max(...busy)] : fallback;
  return hours.filter((h) => h.hour >= from && h.hour <= to);
}

/** "9" … "12" … "8" in Arabic (12-hour clock, design 2215), "09"… "20" in English. */
export function hourLabel(hour: number, locale: AppLocale): string {
  if (locale === 'en') return String(hour).padStart(2, '0');
  const twelve = hour % 12 === 0 ? 12 : hour % 12;
  return String(twelve);
}

/* ---------------------------------------------------------------- calendar */

export type Interval = { start: string; end: string };

const MINUTE = 60_000;

export function minutesBetween(from: string | number, to: string | number): number {
  return Math.round((new Date(to).getTime() - new Date(from).getTime()) / MINUTE);
}

/** The time axis of a calendar view: from the earliest to the latest window or booking, on whole hours. */
export function axis(
  intervals: Interval[],
  fallbackStart: string,
  fallbackHours = 12,
): { start: number; end: number } {
  if (intervals.length === 0) {
    const start = floorHour(new Date(fallbackStart).getTime());
    return { start, end: start + fallbackHours * 60 * MINUTE };
  }
  const start = Math.min(...intervals.map((i) => new Date(i.start).getTime()));
  const end = Math.max(...intervals.map((i) => new Date(i.end).getTime()));
  return { start: floorHour(start), end: ceilHour(end) };
}

function floorHour(ms: number): number {
  return Math.floor(ms / (60 * MINUTE)) * 60 * MINUTE;
}

function ceilHour(ms: number): number {
  return Math.ceil(ms / (60 * MINUTE)) * 60 * MINUTE;
}

/** Top and height of an interval on an axis, in percent (minute-accurate, DV-S19). */
export function place(
  interval: Interval,
  range: { start: number; end: number },
): { top: number; height: number } {
  const total = range.end - range.start;
  const start = Math.max(new Date(interval.start).getTime(), range.start);
  const end = Math.min(new Date(interval.end).getTime(), range.end);
  return { top: ((start - range.start) / total) * 100, height: (Math.max(end - start, 0) / total) * 100 };
}

/** The complement of `windows` within `range` (shaded as "off shift"). */
export function gaps(windows: Interval[], range: { start: number; end: number }): Interval[] {
  const sorted = [...windows].sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime());
  const result: Interval[] = [];
  let cursor = range.start;
  for (const w of sorted) {
    const s = new Date(w.start).getTime();
    const e = new Date(w.end).getTime();
    if (s > cursor)
      result.push({
        start: new Date(cursor).toISOString(),
        end: new Date(Math.min(s, range.end)).toISOString(),
      });
    cursor = Math.max(cursor, e);
  }
  if (cursor < range.end)
    result.push({ start: new Date(cursor).toISOString(), end: new Date(range.end).toISOString() });
  return result.filter((g) => new Date(g.end).getTime() > new Date(g.start).getTime());
}

/**
 * Side-by-side lanes for overlapping blocks (the week view mixes professionals): each block gets the first lane free at
 * its start; `lanes` is the widest overlap of its group, so blocks share the column width evenly.
 */
export function laneLayout<T extends Interval>(items: T[]): Array<{ item: T; lane: number; lanes: number }> {
  const sorted = [...items].sort(
    (a, b) =>
      new Date(a.start).getTime() - new Date(b.start).getTime() ||
      new Date(b.end).getTime() - new Date(a.end).getTime(),
  );
  const placed: Array<{ item: T; lane: number; lanes: number; group: number }> = [];
  let laneEnds: number[] = [];
  let group = 0;
  let groupEnd = -Infinity;
  for (const item of sorted) {
    const start = new Date(item.start).getTime();
    const end = new Date(item.end).getTime();
    if (start >= groupEnd) {
      group += 1;
      laneEnds = [];
    }
    let lane = laneEnds.findIndex((laneEnd) => laneEnd <= start);
    if (lane === -1) {
      lane = laneEnds.length;
      laneEnds.push(end);
    } else {
      laneEnds[lane] = end;
    }
    groupEnd = Math.max(groupEnd, end);
    placed.push({ item, lane, lanes: 0, group });
  }
  const widths = new Map<number, number>();
  for (const p of placed) widths.set(p.group, Math.max(widths.get(p.group) ?? 0, p.lane + 1));
  return placed.map(({ item, lane, group: g }) => ({ item, lane, lanes: widths.get(g)! }));
}

export const DAY_PARTS = ['morning', 'noon', 'evening', 'night'] as const;
export type DayPart = (typeof DAY_PARTS)[number];

/** The design's day-parts (s-calendar week heatmap): morning to 12:00, noon to 17:00, evening to 21:00, then night. */
export function dayPartOf(localHour: number): DayPart {
  if (localHour >= 5 && localHour < 12) return 'morning';
  if (localHour >= 12 && localHour < 17) return 'noon';
  if (localHour >= 17 && localHour < 21) return 'evening';
  return 'night';
}

/** Density level of a heat cell: closed (null), low, medium or full (design legend). */
export function heatLevel(count: number | null): 'closed' | 'none' | 'low' | 'medium' | 'full' {
  if (count === null) return 'closed';
  if (count === 0) return 'none';
  if (count > 8) return 'full';
  if (count > 4) return 'medium';
  return 'low';
}

/** "سعود ر." — first name and the initial of the last word (the calendar grid is narrow, design 4231). */
export function shortName(full: string): string {
  const words = full.trim().split(/\s+/).filter(Boolean);
  if (words.length <= 1) return words[0] ?? '';
  let last = words[words.length - 1]!;
  if (last.startsWith('ال') && last.length > 2) last = last.slice(2);
  return `${words[0]} ${last[0]!.toUpperCase()}.`;
}

/* ---------------------------------------------------------------- appointments */

/** Status chips (DV-S08): every status, cancellations grouped. */
export const STATUS_CHIPS = [
  'all',
  'pending',
  'confirmed',
  'arrived',
  'completed',
  'cancelled',
  'noShow',
] as const;
export type StatusChip = (typeof STATUS_CHIPS)[number];

export type BookingStatusValue =
  'Pending' | 'Confirmed' | 'Arrived' | 'Completed' | 'CancelledByCustomer' | 'CancelledByShop' | 'NoShow';

export function statusesOf(chip: StatusChip): BookingStatusValue[] {
  switch (chip) {
    case 'pending':
      return ['Pending'];
    case 'confirmed':
      return ['Confirmed'];
    case 'arrived':
      return ['Arrived'];
    case 'completed':
      return ['Completed'];
    case 'cancelled':
      return ['CancelledByCustomer', 'CancelledByShop'];
    case 'noShow':
      return ['NoShow'];
    default:
      return [];
  }
}

export function chipOf(value: string | null | undefined): StatusChip {
  return (STATUS_CHIPS as readonly string[]).includes(value ?? '') ? (value as StatusChip) : 'all';
}

/* ---------------------------------------------------------------- time zones */

/** Minutes the zone is ahead of UTC at an instant. */
export function offsetMinutes(instantMs: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instantMs));
  const get = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value ?? 0);
  const asUtc = Date.UTC(
    get('year'),
    get('month') - 1,
    get('day'),
    get('hour'),
    get('minute'),
    get('second'),
  );
  return Math.round((asUtc - instantMs) / MINUTE);
}

/** The instant of local midnight at the start of a shop-local date (YYYY-MM-DD). */
export function zonedMidnight(date: string, timeZone: string): number {
  const [y, m, d] = date.split('-').map(Number) as [number, number, number];
  const guess = Date.UTC(y, m - 1, d);
  const first = guess - offsetMinutes(guess, timeZone) * MINUTE;
  const second = guess - offsetMinutes(first, timeZone) * MINUTE;
  return second;
}

/**
 * The shared time-of-day axis of calendar days, in minutes after each day's local midnight (may pass 24:00 for windows
 * after midnight), on whole hours; 09:00–21:00 when there is nothing to show.
 */
export function dayAxis(
  days: Array<{ date: string; intervals: Interval[] }>,
  timeZone: string,
): { from: number; to: number } {
  let from = Infinity;
  let to = -Infinity;
  for (const day of days) {
    const midnight = zonedMidnight(day.date, timeZone);
    for (const i of day.intervals) {
      from = Math.min(from, (new Date(i.start).getTime() - midnight) / MINUTE);
      to = Math.max(to, (new Date(i.end).getTime() - midnight) / MINUTE);
    }
  }
  if (!Number.isFinite(from)) return { from: 9 * 60, to: 21 * 60 };
  return { from: Math.floor(from / 60) * 60, to: Math.ceil(to / 60) * 60 };
}

/** The instant range of a day on the shared axis. */
export function dayRange(
  date: string,
  timeZone: string,
  axisMinutes: { from: number; to: number },
): { start: number; end: number } {
  const midnight = zonedMidnight(date, timeZone);
  return { start: midnight + axisMinutes.from * MINUTE, end: midnight + axisMinutes.to * MINUTE };
}
