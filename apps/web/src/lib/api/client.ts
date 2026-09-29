'use client';

import createClient from 'openapi-fetch';
import type { paths } from './schema';

/** Name of the readable (non-HttpOnly) double-submit CSRF cookie issued by the API (D-027). */
export const CSRF_COOKIE = 'trimme-csrf';
export const CSRF_HEADER = 'X-CSRF-Token';

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const AUTH_PATH = '/api/v1/auth/';

function readCookie(name: string): string | undefined {
  const entry = document.cookie
    .split('; ')
    .find((item) => item.startsWith(`${name}=`))
    ?.slice(name.length + 1);
  return entry ? decodeURIComponent(entry) : undefined;
}

/** The CSRF token, fetched lazily the first time a fresh browser makes an unsafe call. */
async function csrfToken(): Promise<string | undefined> {
  const existing = readCookie(CSRF_COOKIE);
  if (existing) return existing;
  await fetch('/api/v1/auth/csrf', { credentials: 'include', cache: 'no-store' });
  return readCookie(CSRF_COOKIE);
}

async function withCsrf(request: Request): Promise<Request> {
  if (!UNSAFE_METHODS.has(request.method)) return request;
  const token = await csrfToken();
  if (!token) return request;
  const next = new Request(request);
  next.headers.set(CSRF_HEADER, token);
  return next;
}

/** `expired`: the server refused the refresh (401/403); `unavailable`: no answer, 429 or 5xx, worth trying later. */
export type RefreshOutcome = 'refreshed' | 'expired' | 'unavailable';

let refreshing: Promise<RefreshOutcome> | null = null;

/**
 * Rotates the session once for every caller that hit a 401 at the same time. A 409 means another tab refreshed
 * a moment ago (cookies are shared), which is as good as success.
 */
export function refreshSessionOutcome(): Promise<RefreshOutcome> {
  refreshing ??= (async (): Promise<RefreshOutcome> => {
    try {
      const token = await csrfToken();
      const response = await fetch('/api/v1/auth/refresh', {
        method: 'POST',
        credentials: 'include',
        cache: 'no-store',
        headers: token ? { [CSRF_HEADER]: token } : undefined,
      });
      if (response.ok || response.status === 409) return 'refreshed';
      return response.status === 401 || response.status === 403 ? 'expired' : 'unavailable';
    } catch {
      return 'unavailable';
    } finally {
      setTimeout(() => {
        refreshing = null;
      }, 0);
    }
  })();
  return refreshing;
}

/** Whether the session could be rotated right now (see `refreshSessionOutcome`). */
export async function refreshSession(): Promise<boolean> {
  return (await refreshSessionOutcome()) === 'refreshed';
}

/** Called when the session cannot be restored; the app shell navigates to sign-in with `returnTo`. */
type SessionExpiredHandler = () => void;
let onSessionExpired: SessionExpiredHandler = () => {};

export function setSessionExpiredHandler(handler: SessionExpiredHandler) {
  onSessionExpired = handler;
}

/**
 * fetch with the cookie-session protocol: adds the CSRF header to unsafe requests, and on a 401 from a non-auth
 * endpoint refreshes the session once and retries; if that fails, reports the expired session.
 */
async function sessionFetch(input: Request): Promise<Response> {
  const retry = input.clone();
  const response = await fetch(await withCsrf(input));
  if (response.status !== 401 || new URL(input.url).pathname.startsWith(AUTH_PATH)) {
    return response;
  }

  if (await refreshSession()) {
    const retried = await fetch(await withCsrf(retry));
    if (retried.status !== 401) return retried;
  }

  onSessionExpired();
  return response;
}

/**
 * Browser API client. Requests go to the same origin (`/api/...`): Nginx routes them to the API in production,
 * and Next.js rewrites them in development. Auth uses HttpOnly cookies only — never Web Storage (spec §9).
 */
export const browserApi = createClient<paths>({
  // Absolute same-origin base: Request objects need absolute URLs outside browsers (tests); never used on the server.
  baseUrl: typeof window === 'undefined' ? '' : window.location.origin,
  credentials: 'include',
  fetch: sessionFetch,
});
