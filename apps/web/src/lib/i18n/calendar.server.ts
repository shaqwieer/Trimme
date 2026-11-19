import 'server-only';
import { cookies } from 'next/headers';
import { CALENDAR_COOKIE, type DisplayCalendar, parseCalendar } from './calendar';

/** The calendar this device's shop user chose (D-151); Gregorian when none was chosen. */
export async function readDisplayCalendar(): Promise<DisplayCalendar> {
  const store = await cookies();
  return parseCalendar(store.get(CALENDAR_COOKIE)?.value);
}
