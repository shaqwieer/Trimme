import { connection } from 'next/server';
import { notFound } from 'next/navigation';

/**
 * Development-only routes (component gallery, shell previews) are unavailable in production builds
 * unless explicitly enabled for local E2E runs with TRIMME_ENABLE_DEV_ROUTES=true. Evaluated per request.
 */
export async function assertDevRoutesEnabled(): Promise<void> {
  await connection();
  const enabled = process.env.NODE_ENV !== 'production' || process.env.TRIMME_ENABLE_DEV_ROUTES === 'true';
  if (!enabled) {
    notFound();
  }
}
