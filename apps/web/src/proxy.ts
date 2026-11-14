import { type NextRequest, NextResponse } from 'next/server';
import createMiddleware from 'next-intl/middleware';
import { routing } from './i18n/routing';
import { optionalReturnTo } from './lib/auth/paths';
import {
  isSignedInGated,
  SIGNED_IN_COOKIE,
  signedInDestination,
  signedInUserType,
} from './lib/auth/signedIn';
import { TILE_URL } from './lib/map/config';
import { buildCsp, createNonce } from './lib/security/csp';
import { siteUrl } from './lib/seo/site';

/** Locale negotiation: `/` → `/ar` (or `/en` from Accept-Language); unprefixed paths get a locale prefix. */
const handleLocale = createMiddleware(routing);

const LOCALE_PATH = new RegExp(`^/(${routing.locales.join('|')})(/.*)?$`);

/**
 * A signed-in user cannot open the landing page or the sign-in and sign-up pages until they sign out (D-144): they go
 * through the session page to the sign-in's `returnTo`, or to their home. The hint cookie only names the user type;
 * the session page and the pages behind it check the real session, and a stale hint is cleared there.
 */
function signedInRedirect(request: NextRequest): NextResponse | null {
  const userType = signedInUserType(request.cookies.get(SIGNED_IN_COOKIE)?.value);
  const match = userType ? LOCALE_PATH.exec(request.nextUrl.pathname) : null;
  const page = match?.[2] ?? '/';
  if (!userType || !match || !isSignedInGated(page)) return null;
  const returnTo =
    page === '/' ? undefined : optionalReturnTo(request.nextUrl.searchParams.get('returnTo') ?? undefined);
  const [pathname, search] = `/${match[1]}${signedInDestination(userType, returnTo)}`.split('?');
  const url = request.nextUrl.clone();
  url.pathname = pathname!;
  url.search = search ? `?${search}` : '';
  return NextResponse.redirect(url);
}

/**
 * Every page gets a fresh script nonce (D-117). Next.js reads it from the request's CSP header while rendering, so the
 * header is set on the request before the locale middleware, which forwards the request headers it is given.
 */
export default function proxy(request: NextRequest) {
  const away = signedInRedirect(request);
  if (away) return away;

  const nonce = createNonce();
  const https = siteUrl().protocol === 'https:';
  const csp = buildCsp({
    nonce,
    development: process.env.NODE_ENV === 'development',
    tileUrl: TILE_URL,
    https,
  });
  request.headers.set('x-nonce', nonce);
  request.headers.set('Content-Security-Policy', csp);

  const response = handleLocale(request);
  response.headers.set('Content-Security-Policy', csp);
  if (https) {
    // The TLS proxy may set HSTS as well; this covers a deployment where it does not. Only ever sent for an HTTPS site.
    response.headers.set('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');
  }
  return response;
}

export const config = {
  // Skip API/hub proxies, Next internals and files with an extension. The dot must reach the regex escaped (`\\.`):
  // a single `\.` is just `.` in a string, which skipped every path longer than one character (D-114: `/q/{code}`).
  matcher: '/((?!api|hubs|_next|_vercel|.*\\..*).*)',
};
