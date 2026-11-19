/**
 * The calendar a shop user reads dates in (D-151, the barber's «حسابي»: ميلادي or هجري). A display preference only,
 * kept in a first-party cookie on this device: the API, date inputs and the month grid stay Gregorian, and so do the
 * public pages. Hijri is the Umm al-Qura calendar used in Saudi Arabia.
 */
export type DisplayCalendar = 'gregory' | 'islamic-umalqura';

export const CALENDAR_COOKIE = 'trimme_calendar';

/** A year, so the choice outlives the session (shop users stay signed in for a year, D-142). */
export const CALENDAR_COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

/** The cookie's value ("hijri"), or Gregorian for anything else. */
export function parseCalendar(value: string | undefined | null): DisplayCalendar {
  return value === 'hijri' ? 'islamic-umalqura' : 'gregory';
}

export function calendarCookieValue(calendar: DisplayCalendar): string {
  return calendar === 'islamic-umalqura' ? 'hijri' : 'gregory';
}
