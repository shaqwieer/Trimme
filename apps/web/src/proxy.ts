import type { NextRequest } from 'next/server';
import createMiddleware from 'next-intl/middleware';
import { routing } from './i18n/routing';
import { TILE_URL } from './lib/map/config';
import { buildCsp, createNonce } from './lib/security/csp';
import { siteUrl } from './lib/seo/site';

/** Locale negotiation: `/` → `/ar` (or `/en` from Accept-Language); unprefixed paths get a locale prefix. */
const handleLocale = createMiddleware(routing);

/**
 * Every page gets a fresh script nonce (D-117). Next.js reads it from the request's CSP header while rendering, so the
 * header is set on the request before the locale middleware, which forwards the request headers it is given.
 */
export default function proxy(request: NextRequest) {
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
