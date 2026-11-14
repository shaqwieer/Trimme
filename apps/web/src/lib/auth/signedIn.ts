import { homeFor, withReturnTo } from './paths';

/**
 * Signed-in users stay out of the landing and sign-in pages until they sign out (D-144). The API sets this cookie with
 * the session (only the user type, no token; it lasts as long as the session) and deletes it on sign-out and on a
 * refresh that cannot restore the session. It grants nothing: pages and endpoints still check the access cookie.
 */
export const SIGNED_IN_COOKIE = 'trimme-signed-in';

/** The locale-less pages a signed-in user is sent away from: the landing page and the sign-in and sign-up steps. */
const GATED = /^\/(?:auth\/(?:sign-in|sign-up|verify|staff\/sign-in)\/?)?$/;

export function isSignedInGated(path: string): boolean {
  return GATED.test(path);
}

/**
 * Where a signed-in user goes instead: through the session page, which refreshes the session when the short-lived
 * access cookie has expired (and clears a stale hint when it cannot), then on to `returnTo` or their home.
 */
export function signedInDestination(userType: string, returnTo: string | undefined): string {
  return withReturnTo('/auth/session', returnTo ?? homeFor(userType));
}

/** The hint's user type, or `undefined` for anything this app does not write. */
export function signedInUserType(value: string | undefined): string | undefined {
  return value && /^(Customer|ShopUser|PlatformAdmin)$/.test(value) ? value : undefined;
}

/** The hint's user type from `document.cookie`. */
export function readSignedInHint(cookies: string): string | undefined {
  return signedInUserType(
    cookies
      .split(';')
      .map((part) => part.trim())
      .find((part) => part.startsWith(`${SIGNED_IN_COOKIE}=`))
      ?.slice(SIGNED_IN_COOKIE.length + 1),
  );
}

/** Forgets the hint in this browser when the session could not be restored, so sign-in is reachable again. */
export function clearSignedInHint(): void {
  document.cookie = `${SIGNED_IN_COOKIE}=; Max-Age=0; Path=/; Secure; SameSite=Lax`;
}
