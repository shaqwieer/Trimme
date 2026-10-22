import type { components } from '@/lib/api/schema';

export type BarberDay = components['schemas']['BarberDayResponse'];
export type MinuteWindow = components['schemas']['MinuteWindowDto'];

/** What 5 minutes of the barber's day are, in the order the cell checks it (D-134, D-137). */
export type SlotState = 'break' | 'shopBreak' | 'booked' | 'off' | 'outside' | 'free';

/** One hour at a glance: the same states, plus an hour that is partly the barber's break or partly free (D-137). */
export type HourState = SlotState | 'partBreak' | 'partFree';

export type MinuteCell = {
  start: number;
  end: number;
  state: SlotState;
  /** The 5 minutes are over: shown, but no longer changed. */
  past: boolean;
  /** A free slot can be picked for a break, and a break can be cancelled. */
  toggles: boolean;
};

export type HourCell = {
  hour: number;
  start: number;
  end: number;
  state: HourState;
  /** How many of its minutes are the barber's own break. */
  breakMinutes: number;
  /** The hour is over. */
  past: boolean;
  /** Tapping opens its minutes: at least one of them can still be picked or cancelled. */
  opens: boolean;
};

export const HOURS_PER_DAY = 24;
/** Breaks are set every 5 minutes, as the booking's times (D-130, D-137). */
export const STEP_MINUTES = 5;

/** From the current 5 minutes on: what is over keeps its break. */
const changeableFrom = (day: BarberDay) => Math.floor(day.passedMinutes / STEP_MINUTES) * STEP_MINUTES;

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
 * What [start, end) is. A break is the barber's own, set here; the shop's breaks, booked time and time off are the
 * owner's or the customers', so they cannot be turned into a break (no booking is ever moved).
 */
function stateOf(day: BarberDay, start: number, end: number): SlotState {
  const working = overlap(day.working, start, end);
  if (overlap(day.breaks, start, end) > 0) return 'break';
  if (working === 0) return 'outside';
  if (overlap(day.shopBreaks, start, end) >= working) return 'shopBreak';
  if (overlap(day.booked, start, end) > 0) return 'booked';
  if (overlap(day.timeOff, start, end) >= working) return 'off';
  return 'free';
}

/** The hour's twelve 5-minute slots, :00 to :55 (D-137). */
export function minuteCells(day: BarberDay, hour: number): MinuteCell[] {
  return Array.from({ length: 60 / STEP_MINUTES }, (_, index) => {
    const start = hour * 60 + index * STEP_MINUTES;
    const end = start + STEP_MINUTES;
    const state = stateOf(day, start, end);
    const past = end <= day.passedMinutes;
    return { start, end, state, past, toggles: !past && (state === 'free' || state === 'break') };
  });
}

function summarize(slots: readonly MinuteCell[]): HourState {
  const working = slots.filter((s) => s.state !== 'outside');
  if (working.length === 0) return 'outside';
  const count = (state: SlotState) => working.filter((s) => s.state === state).length;
  if (count('break') === working.length) return 'break';
  if (count('break') > 0) return 'partBreak';
  if (count('free') === working.length) return 'free';
  if (count('free') > 0) return 'partFree';
  if (count('booked') > 0) return 'booked';
  return count('shopBreak') > 0 ? 'shopBreak' : 'off';
}

/** The day's 24 hours, 00:00 to 24:00, each summing up its twelve 5-minute slots. */
export function hourCells(day: BarberDay): HourCell[] {
  return Array.from({ length: HOURS_PER_DAY }, (_, hour) => {
    const slots = minuteCells(day, hour);
    const start = hour * 60;
    return {
      hour,
      start,
      end: start + 60,
      state: summarize(slots),
      breakMinutes: slots.filter((s) => s.state === 'break').length * STEP_MINUTES,
      past: start + 60 <= day.passedMinutes,
      opens: slots.some((s) => s.toggles),
    };
  });
}

/** Picked 5-minute starts as windows, neighbours joined: [600, 605, 615] → 10:00–10:10 and 10:15–10:20. */
export function slotsToWindows(starts: readonly number[]): MinuteWindow[] {
  return mergeWindows(starts.map((start) => ({ startMinute: start, endMinute: start + STEP_MINUTES })));
}

/** The barber's breaks with these windows added. */
export function addBreaks(day: BarberDay, windows: readonly MinuteWindow[]): MinuteWindow[] {
  return mergeWindows([...day.breaks, ...windows]);
}

/**
 * The part of the barber's own break around `minute` that can still be cancelled: from the current 5 minutes on, so
 * a break already under way keeps what is over.
 */
export function cancellableBreak(day: BarberDay, minute: number): MinuteWindow | undefined {
  const window = day.breaks.find((w) => w.startMinute <= minute && minute < w.endMinute);
  if (!window) return undefined;
  const from = Math.max(window.startMinute, changeableFrom(day));
  return from < window.endMinute ? { startMinute: from, endMinute: window.endMinute } : undefined;
}

/** The barber's breaks without this window. */
export function removeBreak(day: BarberDay, window: MinuteWindow): MinuteWindow[] {
  return withoutRange(day.breaks, window.startMinute, window.endMinute);
}

/** The 5-minute starts of the day that can still become a break. */
export function freeStarts(day: BarberDay): number[] {
  return Array.from({ length: HOURS_PER_DAY }, (_, hour) => minuteCells(day, hour))
    .flat()
    .filter((c) => c.toggles && c.state === 'free')
    .map((c) => c.start);
}

/** Every 5 minutes still free become a break ("the whole day"); booked time stays as it is. */
export function wholeDay(day: BarberDay): MinuteWindow[] {
  return addBreaks(day, slotsToWindows(freeStarts(day)));
}

/** Clears the breaks from the current 5 minutes on. */
export function clearDay(day: BarberDay): MinuteWindow[] {
  return withoutRange(day.breaks, changeableFrom(day), HOURS_PER_DAY * 60);
}

/** Whether "clear all breaks" has anything to clear. */
export function hasBreaksAhead(day: BarberDay): boolean {
  return day.breaks.some((w) => w.endMinute > changeableFrom(day));
}
