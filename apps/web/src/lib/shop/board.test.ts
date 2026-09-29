import { describe, expect, it } from 'vitest';
import { reconnectDelay } from '@/components/shop/live/OperationsLive';
import {
  chipOf,
  dayAxis,
  dayPartOf,
  dayRange,
  gaps,
  heatLevel,
  hourLabel,
  laneLayout,
  loadPercent,
  place,
  shortName,
  statusesOf,
  trimHours,
  zonedMidnight,
} from './board';

const riyadh = 'Asia/Riyadh';
const at = (iso: string) => new Date(iso).toISOString();

describe('shop board helpers (D-100)', () => {
  it('computes the load as booked over bookable minutes, capped, and zero without bookable time', () => {
    expect(loadPercent(60, 660)).toBe(9);
    expect(loadPercent(700, 660)).toBe(100);
    expect(loadPercent(30, 0)).toBe(0);
  });

  it('trims the hourly chart to the busy hours, with a working-day fallback', () => {
    const hours = Array.from({ length: 24 }, (_, hour) => ({
      hour,
      count: hour === 10 ? 2 : hour === 18 ? 5 : 0,
    }));
    expect(trimHours(hours).map((h) => h.hour)).toEqual([10, 11, 12, 13, 14, 15, 16, 17, 18]);
    expect(trimHours(hours.map((h) => ({ ...h, count: 0 }))).map((h) => h.hour)).toEqual([
      9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
    ]);
    expect(hourLabel(18, 'ar')).toBe('6');
    expect(hourLabel(12, 'ar')).toBe('12');
    expect(hourLabel(9, 'en')).toBe('09');
  });

  it('finds local midnight and a shared axis that runs past 24:00 for windows after midnight', () => {
    expect(new Date(zonedMidnight('2026-10-08', riyadh)).toISOString()).toBe('2026-10-07T21:00:00.000Z');
    const axis = dayAxis(
      [
        {
          date: '2026-10-08',
          intervals: [{ start: at('2026-10-08T15:00:00Z'), end: at('2026-10-08T22:00:00Z') }],
        },
        {
          date: '2026-10-09',
          intervals: [{ start: at('2026-10-09T06:10:00Z'), end: at('2026-10-09T18:00:00Z') }],
        },
      ],
      riyadh,
    );
    // 18:00–01:00 (next day) and 09:10–21:00 → 09:00 to 25:00 on every day.
    expect(axis).toEqual({ from: 9 * 60, to: 25 * 60 });
    expect(dayAxis([], riyadh)).toEqual({ from: 540, to: 1260 });
    const range = dayRange('2026-10-08', riyadh, axis);
    expect(new Date(range.end).toISOString()).toBe('2026-10-08T22:00:00.000Z');
  });

  it('places intervals minute-accurately and shades the time outside the windows', () => {
    const range = { start: Date.parse('2026-10-08T06:00:00Z'), end: Date.parse('2026-10-08T08:00:00Z') };
    expect(place({ start: at('2026-10-08T06:30:00Z'), end: at('2026-10-08T06:35:00Z') }, range)).toEqual({
      top: 25,
      height: (5 / 120) * 100,
    });
    expect(place({ start: at('2026-10-08T05:00:00Z'), end: at('2026-10-08T06:30:00Z') }, range).top).toBe(0);
    expect(gaps([{ start: at('2026-10-08T06:30:00Z'), end: at('2026-10-08T07:00:00Z') }], range)).toEqual([
      { start: at('2026-10-08T06:00:00Z'), end: at('2026-10-08T06:30:00Z') },
      { start: at('2026-10-08T07:00:00Z'), end: at('2026-10-08T08:00:00Z') },
    ]);
  });

  it('puts overlapping bookings side by side and gives separate groups the full width', () => {
    const block = (id: string, s: string, e: string) => ({
      id,
      start: at(`2026-10-08T${s}:00Z`),
      end: at(`2026-10-08T${e}:00Z`),
    });
    const layout = laneLayout([
      block('a', '06:00', '06:30'),
      block('b', '06:10', '06:40'),
      block('c', '06:30', '07:00'),
      block('d', '08:00', '08:30'),
    ]);
    const of = (id: string) => layout.find((l) => l.item.id === id)!;
    expect([of('a').lane, of('b').lane, of('c').lane]).toEqual([0, 1, 0]);
    expect(of('a').lanes).toBe(2);
    expect(of('d')).toMatchObject({ lane: 0, lanes: 1 });
  });

  it('matches the design heatmap: day parts and density levels', () => {
    expect([dayPartOf(9), dayPartOf(13), dayPartOf(18), dayPartOf(23), dayPartOf(0)]).toEqual([
      'morning',
      'noon',
      'evening',
      'night',
      'night',
    ]);
    expect([heatLevel(null), heatLevel(0), heatLevel(3), heatLevel(6), heatLevel(9)]).toEqual([
      'closed',
      'none',
      'low',
      'medium',
      'full',
    ]);
  });

  it('shortens names for the grid and maps the status chips (cancellations together)', () => {
    expect(shortName('سعود الرشيد')).toBe('سعود ر.');
    expect(shortName('Khalid')).toBe('Khalid');
    expect(statusesOf('cancelled')).toEqual(['CancelledByCustomer', 'CancelledByShop']);
    expect(statusesOf('all')).toEqual([]);
    expect(chipOf('noShow')).toBe('noShow');
    expect(chipOf('<x>')).toBe('all');
  });

  it('backs off between live reconnection attempts, up to 30 s', () => {
    expect([0, 1, 2, 5, 9].map(reconnectDelay)).toEqual([1_000, 2_000, 4_000, 30_000, 30_000]);
  });
});
