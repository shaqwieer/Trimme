/**
 * The shop calendar's rows (D-160, the client's request): an hour each, or half an hour with «كل نص ساعة» on. A row
 * keeps its height either way, so half-hour rows make the day twice as tall and a short booking easier to read and
 * press. The choice is remembered on the device in a first-party cookie that the server page reads, so the first
 * render already has it. Not HttpOnly: the switch writes it, and it holds only "30" or "60", never a token. Pure, so
 * the server page and the client switch share it.
 */
export const CALENDAR_ROWS_COOKIE = 'trimme_calendar_rows';

/** A year: it is a display preference, kept until changed. */
export const CALENDAR_ROWS_MAX_AGE = 60 * 60 * 24 * 365;

/** A row's length in minutes. */
export type CalendarRowMinutes = 30 | 60;

/** The remembered row length; anything else is the default hour. */
export function parseCalendarRows(value: string | undefined | null): CalendarRowMinutes {
  return value === '30' ? 30 : 60;
}

/** The `document.cookie` assignment for a row length. */
export function calendarRowsCookie(minutes: CalendarRowMinutes, secure: boolean): string {
  return [
    `${CALENDAR_ROWS_COOKIE}=${minutes}`,
    'Path=/',
    `Max-Age=${CALENDAR_ROWS_MAX_AGE}`,
    'SameSite=Lax',
    ...(secure ? ['Secure'] : []),
  ].join('; ');
}

/** The remembered row length from a `document.cookie` string. */
export function calendarRowsFromCookies(cookie: string): CalendarRowMinutes {
  const entry = cookie
    .split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(`${CALENDAR_ROWS_COOKIE}=`));
  return parseCalendarRows(entry?.slice(CALENDAR_ROWS_COOKIE.length + 1));
}
