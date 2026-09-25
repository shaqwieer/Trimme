'use client';

import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';

/** Name of the readable (non-HttpOnly) double-submit CSRF cookie issued by the API from Phase 04 (D-027). */
export const CSRF_COOKIE = 'trimme-csrf';
export const CSRF_HEADER = 'X-CSRF-Token';

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

function readCookie(name: string): string | undefined {
  return document.cookie
    .split('; ')
    .find((entry) => entry.startsWith(`${name}=`))
    ?.slice(name.length + 1);
}

const csrfMiddleware: Middleware = {
  onRequest({ request }) {
    if (UNSAFE_METHODS.has(request.method)) {
      const token = readCookie(CSRF_COOKIE);
      if (token) {
        request.headers.set(CSRF_HEADER, decodeURIComponent(token));
      }
    }
    return request;
  },
};

/**
 * Browser API client. Requests go to the same origin (`/api/...`): Nginx routes them to the API in production,
 * and Next.js rewrites them in development. Auth uses HttpOnly cookies only — never Web Storage (spec §9).
 */
export const browserApi = createClient<paths>({ baseUrl: '', credentials: 'include' });
browserApi.use(csrfMiddleware);
