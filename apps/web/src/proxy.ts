import createMiddleware from 'next-intl/middleware';
import { routing } from './i18n/routing';

/** Locale negotiation: `/` → `/ar` (or `/en` from Accept-Language); unprefixed paths get a locale prefix. */
export default createMiddleware(routing);

export const config = {
  // Skip API/hub proxies, Next internals and files with an extension. The dot must reach the regex escaped (`\\.`):
  // a single `\.` is just `.` in a string, which skipped every path longer than one character (D-114: `/q/{code}`).
  matcher: '/((?!api|hubs|_next|_vercel|.*\\..*).*)',
};
