import { describe, expect, it } from 'vitest';
import {
  addBreaks,
  type BarberDay,
  cancellableBreak,
  clearDay,
  freeStarts,
  hasBreaksAhead,
  hourCells,
  mergeWindows,
  minuteCells,
  removeBreak,
  slotsToWindows,
  wholeDay,
  withoutRange,
} from './barberDay';

const w = (startMinute: number, endMinute: number) => ({ startMinute, endMinute });

/** Works 09:00–13:00; the shop's break 10:00–11:00; booked 11:30–12:00; own break 12:00–13:00. */
const day = (overrides: Partial<BarberDay> = {}): BarberDay => ({
  professionalId: 'p1',
  date: '2026-10-22',
  today: '2026-10-21',
  passedMinutes: 0,
  working: [w(540, 780)],
  breaks: [w(720, 780)],
  shopBreaks: [w(600, 660)],
  booked: [w(690, 720)],
  timeOff: [],
  ...overrides,
});

describe("a barber's day by the hour, then every 5 minutes (D-134, D-137)", () => {
  it('shows all 24 hours and sums up each from its 5-minute slots', () => {
    const cells = hourCells(day());
    expect(cells).toHaveLength(24);
    expect(cells.map((c) => c.state).slice(8, 14)).toEqual([
      'outside',
      'free',
      'shopBreak',
      'partFree', // 11:00–11:30 free, 11:30–12:00 booked: the free half can still be a break
      'break',
      'outside',
    ]);
    expect(cells.filter((c) => c.opens).map((c) => c.hour)).toEqual([9, 11, 12]);
    expect(cells[12]!.breakMinutes).toBe(60);
  });

  it('opens an hour as twelve 5-minute slots, :00 to :55', () => {
    const slots = minuteCells(day(), 11);
    expect(slots.map((s) => s.start)).toEqual(Array.from({ length: 12 }, (_, i) => 660 + i * 5));
    expect(slots.slice(5, 7).map((s) => s.state)).toEqual(['free', 'booked']);
    expect(slots.filter((s) => s.toggles)).toHaveLength(6);
  });

  it('makes picked minutes a break, joining neighbours, and shows the hour as partly a break', () => {
    const d = day();
    expect(slotsToWindows([600 + 15, 600 + 20, 600 + 40, 545])).toEqual([
      w(545, 550),
      w(615, 625),
      w(640, 645),
    ]);
    const added = addBreaks(d, slotsToWindows([555, 560, 565]));
    expect(added).toEqual([w(555, 570), w(720, 780)]);
    const nine = hourCells({ ...d, breaks: added })[9]!;
    expect(nine).toMatchObject({ state: 'partBreak', breakMinutes: 15, opens: true });
    // Right before a booking: 11:25–11:30 stays apart from the 12:00 break, with the booked 11:30–12:00 between.
    expect(addBreaks(d, slotsToWindows([685]))).toEqual([w(685, 690), w(720, 780)]);
  });

  it('cancels a whole break from a tap on any of its minutes, keeping what is over', () => {
    const d = day({ breaks: [w(555, 600), w(720, 780)] });
    expect(cancellableBreak(d, 570)).toEqual(w(555, 600));
    expect(removeBreak(d, w(555, 600))).toEqual([w(720, 780)]);
    expect(cancellableBreak(d, 610)).toBeUndefined();
    // At 09:52, 09:15–09:50 is over: only 09:50–10:00 can still be cancelled.
    const now = { ...d, passedMinutes: 592 };
    expect(cancellableBreak(now, 595)).toEqual(w(590, 600));
    expect(minuteCells(now, 9).find((s) => s.start === 585)).toMatchObject({ past: true, toggles: false });
  });

  it('keeps time that is over, booked time and the shop’s breaks as they are', () => {
    const today = day({ passedMinutes: 600, breaks: [w(540, 600), w(720, 780)] });
    const cells = hourCells(today);
    expect(cells[9]).toMatchObject({ state: 'break', past: true, opens: false });
    expect(wholeDay(day({ breaks: [] }))).toEqual([w(540, 600), w(660, 690), w(720, 780)]);
    expect(freeStarts(day({ booked: [], shopBreaks: [], breaks: [w(540, 780)] }))).toEqual([]);
    expect(clearDay(today)).toEqual([w(540, 600)]);
    expect(clearDay(day())).toEqual([]);
    // Mid-way through 09:00–10:00 at 09:32, clearing frees 09:30 on.
    expect(clearDay(day({ passedMinutes: 572, breaks: [w(540, 600)] }))).toEqual([w(540, 570)]);
    expect(hasBreaksAhead(today)).toBe(true);
    expect(hasBreaksAhead(day({ passedMinutes: 800 }))).toBe(false);
    expect(mergeWindows([w(600, 660), w(540, 600), w(630, 700)])).toEqual([w(540, 700)]);
    expect(withoutRange([w(540, 780)], 600, 660)).toEqual([w(540, 600), w(660, 780)]);
  });

  it('marks a day off and hours past midnight from the day before', () => {
    const off = hourCells(day({ timeOff: [w(540, 780)], breaks: [], booked: [], shopBreaks: [] }));
    expect(off[9]!.state).toBe('off');
    const afterMidnight = hourCells(day({ working: [w(0, 60), w(540, 780)], breaks: [] }));
    expect(afterMidnight[0]!.state).toBe('free');
  });
});
