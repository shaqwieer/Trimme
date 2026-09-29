import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { refreshSession, refreshSessionOutcome } from './client';

describe('refreshSessionOutcome', () => {
  beforeEach(() => {
    document.cookie = 'trimme-csrf=token';
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const answer = (status: number) =>
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(null, { status }))),
    );

  it.each([
    [200, 'refreshed'],
    [409, 'refreshed'],
    [401, 'expired'],
    [403, 'expired'],
    [429, 'unavailable'],
    [500, 'unavailable'],
    [503, 'unavailable'],
  ] as const)('maps HTTP %i to %s', async (status, outcome) => {
    answer(status);
    expect(await refreshSessionOutcome()).toBe(outcome);
    await new Promise((resolve) => setTimeout(resolve, 0));
  });

  it('treats a network failure as unavailable, and refreshSession as not refreshed', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    );
    expect(await refreshSessionOutcome()).toBe('unavailable');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(await refreshSession()).toBe(false);
  });
});
