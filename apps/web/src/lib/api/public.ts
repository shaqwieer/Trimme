import 'server-only';
import { headers } from 'next/headers';
import createClient from 'openapi-fetch';
import { clientIp } from './forwarding';
import type { paths } from './schema';
import { apiInternalUrl } from './server';

/**
 * API client for public pages rendered on the server (D-093, D-094). It sends **no cookies**, so the request is anonymous
 * and the API's public response cache can answer it; the same public data is shown to everyone. It forwards the
 * visitor's address (`X-Forwarded-For`) so the API's rate limits count visitors, not the web server, and it never caches
 * in Next.js itself: freshness is the API's job (evicted when shops, services or professionals change).
 */
export async function getPublicApi() {
  const incoming = await headers();
  const forwarded: Record<string, string> = {};
  const ip = clientIp(incoming);
  if (ip) {
    forwarded['x-forwarded-for'] = ip;
  }
  const correlationId = incoming.get('x-correlation-id');
  if (correlationId) {
    forwarded['x-correlation-id'] = correlationId;
  }

  return createClient<paths>({
    baseUrl: apiInternalUrl(),
    headers: forwarded,
    fetch: (request: Request) => fetch(request, { cache: 'no-store' }),
  });
}

/** Thrown when a public API call fails for a reason other than "not found", so the route's error boundary shows. */
export class PublicApiError extends Error {
  constructor(
    public readonly status: number,
    path: string,
  ) {
    super(`Public API ${path} answered ${status}`);
  }
}

/**
 * The data of a successful call, `null` for 404 (an unpublished shop or professional), and a thrown
 * {@link PublicApiError} otherwise.
 */
export function dataOrNull<T>(result: { data?: T; response: Response }, path: string): T | null {
  if (result.response.ok && result.data !== undefined) return result.data;
  if (result.response.status === 404) return null;
  throw new PublicApiError(result.response.status, path);
}
