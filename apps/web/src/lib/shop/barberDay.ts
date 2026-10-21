import type { components } from '@/lib/api/schema';

export type BarberDay = components['schemas']['BarberDayResponse'];
export type MinuteWindow = components['schemas']['MinuteWindowDto'];

/** What one hour of the barber's day is, in the order the cell checks it (D-134). */
export type HourState = 'break' | 'shopBreak' | 'booked' | 'off' | 'outside' | 'free';

export type HourCell = {
  hour: number;
  start: number;
  end: number;
  state: HourState;
  /** The hour is over: shown, but no longer changed. */
  past: boolean;
  /** Tapping turns a free hour into a break or a break back into a free hour. */
  toggles: boolean;
};

export const HOURS_PER_DAY = 24;

function overlap(windows: readonly MinuteWindow[], start: number, end: number): number {
  return windows.reduce(
    (sum, w) => sum + Math.max(0, Math.min(end, w.endMinute) - Math.max(start, w.startMinute)),
    0,
  );
}

/** Sorted, with overlapping and touching windows joined (as the API stores them). */
export function mergeWindows(windows: readonly MinuteWindow[]): MinuteWindow[] {
  const merged: MinuteWindow[] = [];
  for (const w of [...windows].sort((a, b) => a.startMinute - b.startMinute)) {
    const last = merged.at(-1);
    if (last && w.startMinute <= last.endMinute) last.endMinute = Math.max(last.endMinute, w.endMinute);
    else merged.push({ ...w });
  }
  return merged;
}

/** The windows without [start, end). */
export function withoutRange(windows: readonly MinuteWindow[], start: number, end: number): MinuteWindow[] {
  return windows.flatMap((w) =>
    [
      { startMinute: w.startMinute, endMinute: Math.min(w.endMinute, start) },
      { startMinute: Math.max(w.startMinute, end), endMinute: w.endMinute },
    ].filter((part) => part.endMinute > part.startMinute),
  );
}

/**
 * The day's 24 hours, 00:00 to 24:00. A break is the barber's own, set here; the shop's breaks, booked time and time
 * off are the owner's or the customers', so those hours cannot be turned into a break (no booking is ever moved).
 */
export function hourCells(day: BarberDay): HourCell[] {
  return Array.from({ length: HOURS_PER_DAY }, (_, hour) => {
    const start = hour * 60;
    const end = start + 60;
    const working = overlap(day.working, start, end);
    const state: HourState =
      overlap(day.breaks, start, end) > 0
        ? 'break'
        : working === 0
          ? 'outside'
          : overlap(day.shopBreaks, start, end) >= working
            ? 'shopBreak'
            : overlap(day.booked, start, end) > 0
              ? 'booked'
              : overlap(day.timeOff, start, end) >= working
                ? 'off'
                : 'free';
    const past = end <= day.passedMinutes;
    return { hour, start, end, state, past, toggles: !past && (state === 'free' || state === 'break') };
  });
}

/** The barber's breaks after tapping one hour. */
export function toggleHour(day: BarberDay, cell: HourCell): MinuteWindow[] {
  return cell.state === 'break'
    ? withoutRange(day.breaks, cell.start, cell.end)
    : mergeWindows([...day.breaks, { startMinute: cell.start, endMinute: cell.end }]);
}

/** Every hour still free becomes a break ("the whole day"); booked hours stay as they are. */
export function wholeDay(day: BarberDay): MinuteWindow[] {
  const free = hourCells(day).filter((c) => c.toggles && c.state === 'free');
  return mergeWindows([...day.breaks, ...free.map((c) => ({ startMinute: c.start, endMinute: c.end }))]);
}

/** Clears the breaks from the current hour on; an hour already over keeps its break. */
export function clearDay(day: BarberDay): MinuteWindow[] {
  return withoutRange(day.breaks, Math.floor(day.passedMinutes / 60) * 60, HOURS_PER_DAY * 60);
}
