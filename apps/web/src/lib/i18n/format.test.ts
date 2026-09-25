import { describe, expect, it } from 'vitest';
import {
  formatDate,
  formatDayNumber,
  formatDistanceKm,
  formatDurationMinutes,
  formatNumber,
  formatPhone,
  formatPrice,
  formatRating,
  formatTime,
} from './format';

// 2026-09-18 17:30 in Riyadh (UTC+3) — a Friday.
const appointment = '2026-09-18T14:30:00Z';

describe('formatters_ar_en (R-WEB-06, D-040)', () => {
  it('formats clock times with Arabic-Indic digits in Arabic and Latin in English', () => {
    expect(formatTime(appointment, 'ar')).toBe('٥:٣٠ م');
    expect(formatTime(appointment, 'en').toLowerCase()).toBe('5:30 pm');
  });

  it('converts UTC instants to Asia/Riyadh', () => {
    expect(formatTime('2026-09-18T21:15:00Z', 'en').toLowerCase()).toBe('12:15 am');
  });

  it('uses the Gregorian calendar with Arabic weekday names (not Hijri)', () => {
    expect(formatDate(appointment, 'ar')).toBe('الجمعة، ١٨ سبتمبر');
    expect(formatDate(appointment, 'ar', { withYear: true })).toContain('٢٠٢٦');
    expect(formatDate(appointment, 'en')).toBe('Friday 18 September');
  });

  it('keeps calendar day numbers in Latin digits', () => {
    expect(formatDayNumber(appointment, 'ar')).toBe('18');
  });

  it('formats prices with Latin digits and the local currency label', () => {
    expect(formatPrice(85, 'ar')).toBe('85 ر.س');
    expect(formatPrice(85, 'en')).toBe('SAR 85');
    expect(formatPrice(62.5, 'ar')).toBe('62.5 ر.س');
    expect(formatPrice(2400, 'en')).toBe('SAR 2,400');
  });

  it('formats distance, rating and counts with Latin digits', () => {
    expect(formatDistanceKm(2.44, 'ar')).toBe('2.4 كم');
    expect(formatDistanceKm(2.44, 'en')).toBe('2.4 km');
    expect(formatRating(4.8, 'ar')).toBe('4.8');
    expect(formatRating(5, 'en')).toBe('5.0');
    expect(formatNumber(1280, 'ar')).toBe('1,280');
  });

  it('formats durations with the clock-time digit rule', () => {
    expect(formatDurationMinutes(30, 'ar')).toBe('٣٠ دقيقة');
    expect(formatDurationMinutes(30, 'en')).toBe('30 min');
  });

  it('groups Saudi E.164 mobiles for display and leaves other numbers unchanged', () => {
    expect(formatPhone('+966512345678')).toBe('+966 51 234 5678');
    expect(formatPhone('+971501234567')).toBe('+971501234567');
  });
});
