import 'server-only';
import { cookies } from 'next/headers';
import { type ChosenLocation, LOCATION_COOKIE, parseLocation } from './location';

/** The location the customer chose on this device, if any (read from the first-party cookie). */
export async function readLocation(): Promise<ChosenLocation | null> {
  const store = await cookies();
  return parseLocation(store.get(LOCATION_COOKIE)?.value);
}
