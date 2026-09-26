/**
 * Auth routing helpers shared by server guards and client pages. Paths are locale-less (`/account/security`);
 * the next-intl navigation helpers add the locale prefix.
 */

export type UserType = 'Customer' | 'ShopUser' | 'PlatformAdmin';

const MAX_RETURN_TO = 512;
const LOCALE_PREFIX = /^\/(ar|en)(?=\/|\?|#|$)/;

/**
 * Accepts only a same-origin relative path, so `returnTo` can never become an open redirect
 * (`//evil.example`, `https://…`, `/\evil`, control characters and over-long values are rejected).
 */
export function safeReturnTo(value: string | null | undefined, fallback: string): string {
  if (!value || value.length > MAX_RETURN_TO) return fallback;
  if (!value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return fallback;
  if (/[\u0000-\u001f\u007f]/.test(value)) return fallback;
  const withoutLocale = value.replace(LOCALE_PREFIX, '');
  return withoutLocale === '' ? '/' : withoutLocale;
}

/** Where each user type lands after signing in when there is no `returnTo`. */
export function homeFor(userType: string): string {
  switch (userType) {
    case 'PlatformAdmin':
      return '/admin';
    case 'ShopUser':
      return '/shop';
    default:
      return '/account';
  }
}

/** Staff areas use the email + password sign-in; everything else uses the customer mobile sign-in. */
export function signInPathFor(returnTo: string): string {
  return /^\/(admin|shop)(\/|\?|$)/.test(returnTo) ? '/auth/staff/sign-in' : '/auth/sign-in';
}

export function withReturnTo(path: string, returnTo: string | undefined): string {
  return returnTo ? `${path}?returnTo=${encodeURIComponent(returnTo)}` : path;
}

/** Reads an optional `returnTo` search parameter; anything unsafe is dropped. */
export function optionalReturnTo(value: string | string[] | undefined): string | undefined {
  const single = Array.isArray(value) ? value[0] : value;
  return safeReturnTo(single, '') || undefined;
}

export function firstParam(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}
