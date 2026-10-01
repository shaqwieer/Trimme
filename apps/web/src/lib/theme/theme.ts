/**
 * Colour theme preference (D-124). Pure, so the server layout and the client provider share it.
 *
 * The cookie is the source of truth: the server reads it to render `data-theme` on <html> before the first paint (no
 * flash, no hydration mismatch, no inline script under the nonce CSP). `system` renders no attribute, and the CSS
 * follows `prefers-color-scheme` directly, so an OS change applies immediately. A BroadcastChannel carries a change to
 * the other open tabs; nothing but the cookie decides the first render.
 */
export const THEME_PREFERENCES = ['system', 'light', 'dark'] as const;
export type ThemePreference = (typeof THEME_PREFERENCES)[number];

export const DEFAULT_THEME: ThemePreference = 'system';
export const THEME_COOKIE = 'trimme-theme';
/** BroadcastChannel name for cross-tab sync. */
export const THEME_CHANNEL = 'trimme-theme';
/** One year: the choice survives browser restarts. */
export const THEME_COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

export function parseTheme(value: string | null | undefined): ThemePreference {
  return (THEME_PREFERENCES as readonly string[]).includes(value ?? '')
    ? (value as ThemePreference)
    : DEFAULT_THEME;
}

/** The `data-theme` attribute for <html>: only an explicit choice gets one. */
export function themeAttribute(preference: ThemePreference): 'light' | 'dark' | undefined {
  return preference === 'system' ? undefined : preference;
}

/** The `color-scheme` the page declares (also emitted as a meta tag so the browser's canvas matches before CSS). */
export function colorScheme(preference: ThemePreference): 'light' | 'dark' | 'light dark' {
  return preference === 'system' ? 'light dark' : preference;
}

/** `document.cookie` assignment for the preference. Not HttpOnly: the client writes it; it holds no secret. */
export function themeCookie(preference: ThemePreference, secure: boolean): string {
  return [
    `${THEME_COOKIE}=${preference}`,
    'Path=/',
    `Max-Age=${THEME_COOKIE_MAX_AGE}`,
    'SameSite=Lax',
    ...(secure ? ['Secure'] : []),
  ].join('; ');
}

/** Reads the preference from a `document.cookie` string. */
export function themeFromCookieString(cookie: string): ThemePreference {
  const match = cookie
    .split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(`${THEME_COOKIE}=`));
  return parseTheme(match?.slice(THEME_COOKIE.length + 1));
}
