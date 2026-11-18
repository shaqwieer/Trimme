import { describe, expect, it } from 'vitest';
import {
  formatDate,
  formatDayNumber,
  formatDistanceKm,
  formatDurationMinutes,
  formatHour,
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

  // D-148: Arabic uses Arabic-Indic digits everywhere, and the new Riyal sign (U+20C1) after the number.
  it('writes calendar day numbers with Arabic-Indic digits in Arabic', () => {
    expect(formatDayNumber(appointment, 'ar')).toBe('١٨');
    expect(formatDayNumber(appointment, 'en')).toBe('18');
  });

  it('formats prices with the Riyal sign in Arabic and SAR in English', () => {
    expect(formatPrice(85, 'ar')).toBe('٨٥ \u20C1');
    expect(formatPrice(85, 'en')).toBe('SAR 85');
    expect(formatPrice(62.5, 'ar')).toBe('٦٢٫٥ \u20C1');
    expect(formatPrice(2400, 'en')).toBe('SAR 2,400');
  });

  it('formats distance, rating and counts with Arabic-Indic digits in Arabic, Latin in English', () => {
    expect(formatDistanceKm(2.44, 'ar')).toBe('٢٫٤ كم');
    expect(formatDistanceKm(2.44, 'en')).toBe('2.4 km');
    expect(formatRating(4.8, 'ar')).toBe('٤٫٨');
    expect(formatRating(5, 'en')).toBe('5.0');
    expect(formatNumber(1280, 'ar')).toBe('١٬٢٨٠');
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

describe('formatters_tr', () => {
  it('uses the 24-hour clock with Latin digits', () => {
    expect(formatTime(appointment, 'tr')).toBe('17:30');
    expect(formatTime('2026-09-18T06:05:00Z', 'tr')).toBe('09:05');
    expect(formatHour(appointment, 'tr')).toBe('17');
  });

  it('writes Gregorian dates in Turkish', () => {
    expect(formatDate(appointment, 'tr')).toBe('18 Eylül Cuma');
  });

  it('puts the number first, with the Turkish decimal comma and the SAR label', () => {
    expect(formatPrice(85, 'tr')).toBe('85 SAR');
    expect(formatPrice(62.5, 'tr')).toBe('62,5 SAR');
    expect(formatPrice(2400, 'tr')).toBe('2.400 SAR');
    expect(formatDistanceKm(2.44, 'tr')).toBe('2,4 km');
    expect(formatDurationMinutes(30, 'tr')).toBe('30 dk');
  });
});
