import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const notFound = vi.fn(() => {
  throw new Error('NEXT_NOT_FOUND');
});

vi.mock('next/navigation', () => ({ notFound }));
vi.mock('next/server', () => ({ connection: vi.fn(async () => undefined) }));

describe('assertDevRoutesEnabled (D-045)', () => {
  beforeEach(() => {
    notFound.mockClear();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('blocks development-only routes in production builds', async () => {
    vi.stubEnv('NODE_ENV', 'production');
    vi.stubEnv('TRIMME_ENABLE_DEV_ROUTES', '');
    const { assertDevRoutesEnabled } = await import('./devRoutes');

    await expect(assertDevRoutesEnabled()).rejects.toThrow('NEXT_NOT_FOUND');
    expect(notFound).toHaveBeenCalledOnce();
  });

  it('allows them in production only with the explicit opt-in flag', async () => {
    vi.stubEnv('NODE_ENV', 'production');
    vi.stubEnv('TRIMME_ENABLE_DEV_ROUTES', 'true');
    const { assertDevRoutesEnabled } = await import('./devRoutes');

    await expect(assertDevRoutesEnabled()).resolves.toBeUndefined();
    expect(notFound).not.toHaveBeenCalled();
  });

  it('does not treat other flag values as opt-in', async () => {
    vi.stubEnv('NODE_ENV', 'production');
    vi.stubEnv('TRIMME_ENABLE_DEV_ROUTES', 'yes');
    const { assertDevRoutesEnabled } = await import('./devRoutes');

    await expect(assertDevRoutesEnabled()).rejects.toThrow('NEXT_NOT_FOUND');
  });

  it('allows them during local development', async () => {
    vi.stubEnv('NODE_ENV', 'development');
    const { assertDevRoutesEnabled } = await import('./devRoutes');

    await expect(assertDevRoutesEnabled()).resolves.toBeUndefined();
  });
});
