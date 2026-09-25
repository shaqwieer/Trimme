import 'server-only';
import { cookies, headers } from 'next/headers';
import createClient from 'openapi-fetch';
import type { paths } from './schema';

/** Internal API address for server-side calls (container network in Docker, localhost in development). */
export function apiInternalUrl(): string {
  return process.env.TRIMME_API_INTERNAL_URL ?? 'http://localhost:8080';
}

/**
 * Server Component / Route Handler API client. Forwards the caller's cookies (session), language and
 * correlation ID so the API authorizes and logs the request as the real user. Never cached across users.
 */
export async function getServerApi() {
  const cookieStore = await cookies();
  const incoming = await headers();

  const forwarded: Record<string, string> = {};
  const cookieHeader = cookieStore.toString();
  if (cookieHeader) {
    forwarded.cookie = cookieHeader;
  }
  const language = incoming.get('accept-language');
  if (language) {
    forwarded['accept-language'] = language;
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
