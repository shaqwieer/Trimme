import 'server-only';
import { cache } from 'react';
import { redirect } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import type { components } from '@/lib/api/schema';
import { homeFor, withReturnTo } from './paths';

export type Me = components['schemas']['MeResponse'];

/**
 * The signed-in user for this request, or `null` when the access cookie is missing or rejected.
 * Deduplicated per request; never cached across users (the API client uses `no-store`).
 */
export const getMe = cache(async (): Promise<Me | null> => {
  const api = await getServerApi();
  const { data, response } = await api.GET('/api/v1/me');
  if (response.status === 401) return null;
  if (!data) throw new Error(`GET /api/v1/me failed with status ${response.status}`);
  return data;
});

/**
 * Server-side guard for private pages. A missing or expired access cookie sends the browser to the client-side
 * session page, which tries a silent refresh (the refresh cookie is only sent to `/api/v1/auth`, never to pages)
 * and then returns to `returnTo` or asks the user to sign in.
 */
export async function requireUser(locale: string, returnTo: string): Promise<Me> {
  const me = await getMe();
  if (!me) {
    return redirect({ href: withReturnTo('/auth/session', returnTo), locale: asLocale(locale) });
  }
  return me;
}

/** Sign-in pages send an already signed-in user straight on. */
export async function redirectIfSignedIn(locale: string, returnTo: string | undefined): Promise<void> {
  const me = await getMe();
  if (me) {
    redirect({ href: returnTo ?? homeFor(me.userType), locale: asLocale(locale) });
  }
}

/**
 * Guard for the customer's own pages: a signed-in customer with a complete profile (name), else the complete-profile
 * step with `returnTo`. Returns `null` for staff accounts, which the page answers with a permission notice.
 */
export async function requireCustomer(locale: string, returnTo: string): Promise<Me | null> {
  const me = await requireUser(locale, returnTo);
  if (me.userType !== 'Customer') return null;
  if (!me.profileComplete) {
    redirect({ href: withReturnTo('/auth/complete-profile', returnTo), locale: asLocale(locale) });
  }
  return me;
}
