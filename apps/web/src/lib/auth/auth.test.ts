import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { homeFor, optionalReturnTo, safeReturnTo, signInPathFor, withReturnTo } from './paths';

describe('returnTo is same-origin only (open-redirect guard)', () => {
  it.each([
    ['/account/security', '/account/security'],
    ['/ar/account', '/account'],
    ['/en', '/'],
    ['/shops/abc?step=review', '/shops/abc?step=review'],
  ])('keeps the relative path %s', (input, expected) => {
    expect(safeReturnTo(input, '/fallback')).toBe(expected);
  });

  it.each([
    'https://evil.example/',
    '//evil.example',
    '/\\evil.example',
    'javascript:alert(1)',
    'account',
    '/account\u0000',
    `/${'a'.repeat(600)}`,
    '',
  ])('rejects %j', (input) => {
    expect(safeReturnTo(input, '/fallback')).toBe('/fallback');
    expect(optionalReturnTo(input)).toBeUndefined();
  });

  it('routes staff areas to the staff sign-in and everything else to the mobile sign-in', () => {
    expect(signInPathFor('/admin/shops')).toBe('/auth/staff/sign-in');
    expect(signInPathFor('/shop')).toBe('/auth/staff/sign-in');
    expect(signInPathFor('/shops/barber-house')).toBe('/auth/sign-in');
    expect(signInPathFor('/account/security')).toBe('/auth/sign-in');
  });

  it('sends each user type to its home and encodes returnTo', () => {
    expect(homeFor('PlatformAdmin')).toBe('/admin');
    expect(homeFor('ShopUser')).toBe('/shop');
    expect(homeFor('Customer')).toBe('/discover');
    expect(withReturnTo('/auth/sign-in', '/a?b=c')).toBe('/auth/sign-in?returnTo=%2Fa%3Fb%3Dc');
  });
});

describe('browser session fetch', () => {
  const calls: Array<{ url: string; method: string; csrf: string | null }> = [];
  let responses: Array<(url: string) => Response | undefined>;

  beforeEach(() => {
    vi.resetModules();
    calls.length = 0;
    responses = [];
    document.cookie = 'trimme-csrf=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: Request | string, init?: RequestInit) => {
        const request =
          typeof input === 'string' ? new Request(new URL(input, 'http://localhost'), init) : input;
        const url = new URL(request.url).pathname;
        calls.push({ url, method: request.method, csrf: request.headers.get('X-CSRF-Token') });
        if (url === '/api/v1/auth/csrf') {
          document.cookie = 'trimme-csrf=token-1; path=/';
          return new Response(null, { status: 200 });
        }
        for (const respond of responses) {
          const response = respond(url);
          if (response) return response;
        }
        return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
      }),
    );
  });

  afterEach(() => vi.unstubAllGlobals());

  it('fetches the CSRF cookie lazily and sends it on unsafe requests only', async () => {
    const { browserApi } = await import('@/lib/api/client');

    await browserApi.GET('/api/v1/me');
    await browserApi.POST('/api/v1/auth/sessions/revoke-all');

    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual([
      'GET /api/v1/me',
      'GET /api/v1/auth/csrf',
      'POST /api/v1/auth/sessions/revoke-all',
    ]);
    expect(calls[0]?.csrf).toBeNull();
    expect(calls[2]?.csrf).toBe('token-1');
  });

  it('refreshes once on 401 and retries the original request', async () => {
    let meCalls = 0;
    responses.push((url) => {
      if (url === '/api/v1/me') {
        meCalls += 1;
        return meCalls === 1 ? new Response('{}', { status: 401 }) : undefined;
      }
      if (url === '/api/v1/auth/refresh') return new Response(null, { status: 204 });
      return undefined;
    });
    const { browserApi, setSessionExpiredHandler } = await import('@/lib/api/client');
    const expired = vi.fn();
    setSessionExpiredHandler(expired);

    const result = await browserApi.GET('/api/v1/me');

    expect(result.response.status).toBe(200);
    expect(calls.filter((c) => c.url === '/api/v1/auth/refresh')).toHaveLength(1);
    expect(expired).not.toHaveBeenCalled();
  });

  it('reports an expired session when the refresh fails', async () => {
    responses.push((url) =>
      url === '/api/v1/me' || url === '/api/v1/auth/refresh'
        ? new Response('{}', { status: 401 })
        : undefined,
    );
    const { browserApi, setSessionExpiredHandler } = await import('@/lib/api/client');
    const expired = vi.fn();
    setSessionExpiredHandler(expired);

    const result = await browserApi.GET('/api/v1/me');

    expect(result.response.status).toBe(401);
    expect(expired).toHaveBeenCalledOnce();
  });

  it('never tries to refresh for the auth endpoints themselves', async () => {
    responses.push((url) =>
      url === '/api/v1/auth/staff/sign-in' ? new Response('{}', { status: 401 }) : undefined,
    );
    const { browserApi } = await import('@/lib/api/client');

    await browserApi.POST('/api/v1/auth/staff/sign-in', { body: { email: 'a@b.test', password: 'x' } });

    expect(calls.some((c) => c.url === '/api/v1/auth/refresh')).toBe(false);
  });
});
