import { describe, expect, it } from 'vitest';
import { asReportPeriod, reportRange } from './reportPeriod';

describe('reportRange', () => {
  it('covers one day', () => {
    expect(reportRange('day', '2026-11-18')).toEqual({
      from: '2026-11-18',
      to: '2026-11-18',
      previous: '2026-11-17',
      next: '2026-11-19',
    });
  });

  it('covers the week from Sunday to Saturday', () => {
    // 2026-11-18 is a Wednesday.
    expect(reportRange('week', '2026-11-18')).toEqual({
      from: '2026-11-15',
      to: '2026-11-21',
      previous: '2026-11-08',
      next: '2026-11-22',
    });
  });

  it('covers the calendar month, February included', () => {
    expect(reportRange('month', '2026-02-10')).toEqual({
      from: '2026-02-01',
      to: '2026-02-28',
      previous: '2026-01-31',
      next: '2026-03-01',
    });
  });

  it('falls back to a day for an unknown period', () => {
    expect(asReportPeriod('year')).toBe('day');
    expect(asReportPeriod('month')).toBe('month');
  });
});
