import 'server-only';
import { cookies } from 'next/headers';

/** The API's short-lived HttpOnly access cookie (D-052), sent to every same-origin path. */
const ACCESS_COOKIE = 'trimme-access';

/**
 * Whether the request carries an access cookie at all. Only a hint: it says nothing about whether the session is still
 * valid, so it is used to skip a probe that would certainly fail (an anonymous visitor's favorites, Phase 17), never
 * to grant anything.
 */
export async function mayBeSignedIn(): Promise<boolean> {
  return (await cookies()).has(ACCESS_COOKIE);
}
