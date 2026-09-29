'use client';

import {
  type ChosenLocation,
  LOCATION_COOKIE,
  LOCATION_MAX_AGE_SECONDS,
  parseLocation,
  serializeLocation,
} from './location';

function cookieAttributes(maxAge: number): string {
  const secure = typeof location !== 'undefined' && location.protocol === 'https:' ? '; Secure' : '';
  return `; Path=/; Max-Age=${maxAge}; SameSite=Lax${secure}`;
}

/** Remembers the customer's location on this device (a first-party cookie; nothing is stored on the server). */
export function saveLocation(value: ChosenLocation) {
  document.cookie = `${LOCATION_COOKIE}=${serializeLocation(value)}${cookieAttributes(LOCATION_MAX_AGE_SECONDS)}`;
}

export function clearLocation() {
  document.cookie = `${LOCATION_COOKIE}=${cookieAttributes(0)}`;
}

/** The raw cookie value (a stable string, so it can be a `useSyncExternalStore` snapshot). */
export function readLocationCookieRaw(): string | null {
  const entry = document.cookie
    .split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(`${LOCATION_COOKIE}=`));
  return entry ? entry.slice(LOCATION_COOKIE.length + 1) : null;
}

export function readLocationCookie(): ChosenLocation | null {
  return parseLocation(readLocationCookieRaw());
}
