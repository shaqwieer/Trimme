import { describe, expect, it } from 'vitest';
import {
  type BarberDay,
  clearDay,
  hourCells,
  mergeWindows,
  toggleHour,
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

describe("a barber's day by the hour (D-134)", () => {
  it('shows all 24 hours and what each one is', () => {
    const cells = hourCells(day());
    expect(cells).toHaveLength(24);
    expect(cells.map((c) => c.state).slice(8, 14)).toEqual([
      'outside',
      'free',
      'shopBreak',
      'booked',
      'break',
      'outside',
    ]);
    expect(cells.filter((c) => c.toggles).map((c) => c.hour)).toEqual([9, 12]);
  });

  it('turns a free hour into a break and back, joining neighbours', () => {
    const d = day();
    const nine = hourCells(d)[9]!;
    const added = toggleHour(d, nine);
    expect(added).toEqual([w(540, 600), w(720, 780)]);
    expect(toggleHour({ ...d, breaks: added }, { ...nine, state: 'break' })).toEqual([w(720, 780)]);
    expect(mergeWindows([w(600, 660), w(540, 600), w(630, 700)])).toEqual([w(540, 700)]);
    expect(withoutRange([w(540, 780)], 600, 660)).toEqual([w(540, 600), w(660, 780)]);
  });

  it('keeps hours that are over, and booked hours, as they are', () => {
    const today = day({ passedMinutes: 600, breaks: [w(540, 600), w(720, 780)] });
    const cells = hourCells(today);
    expect(cells[9]).toMatchObject({ state: 'break', past: true, toggles: false });
    expect(wholeDay(day({ breaks: [] }))).toEqual([w(540, 600), w(720, 780)]);
    expect(clearDay(today)).toEqual([w(540, 600)]);
    expect(clearDay(day())).toEqual([]);
  });

  it('marks a day off and hours past midnight from the day before', () => {
    const off = hourCells(day({ timeOff: [w(540, 780)], breaks: [], booked: [], shopBreaks: [] }));
    expect(off[9]!.state).toBe('off');
    const afterMidnight = hourCells(day({ working: [w(0, 60), w(540, 780)], breaks: [] }));
    expect(afterMidnight[0]!.state).toBe('free');
  });
});
