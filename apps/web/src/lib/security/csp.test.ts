import { buildCsp, createNonce, tileOrigin } from './csp';

const base = {
  nonce: 'abc123',
  development: false,
  tileUrl: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  https: true,
};

function directive(csp: string, name: string): string[] | undefined {
  const found = csp.split('; ').find((part) => part.split(' ')[0] === name);
  return found?.split(' ').slice(1);
}

describe('content security policy', () => {
  it('allows scripts only by nonce, with strict-dynamic and no inline or eval in production', () => {
    const csp = buildCsp(base);
    expect(directive(csp, 'script-src')).toEqual(["'self'", "'nonce-abc123'", "'strict-dynamic'"]);
    expect(csp).not.toContain('unsafe-eval');
    expect(directive(csp, 'object-src')).toEqual(["'none'"]);
    expect(directive(csp, 'frame-ancestors')).toEqual(["'none'"]);
    expect(directive(csp, 'base-uri')).toEqual(["'self'"]);
    expect(directive(csp, 'upgrade-insecure-requests')).toEqual([]);
  });

  it('allows the map tile host for images and fetches, and blob workers for MapLibre', () => {
    const csp = buildCsp(base);
    expect(directive(csp, 'img-src')).toContain('https://tile.openstreetmap.org');
    expect(directive(csp, 'connect-src')).toContain('https://tile.openstreetmap.org');
    expect(directive(csp, 'worker-src')).toEqual(["'self'", 'blob:']);
  });

  it('adds eval and the dev socket only in development, and no upgrade over plain HTTP', () => {
    const csp = buildCsp({ ...base, development: true, https: false });
    expect(directive(csp, 'script-src')).toContain("'unsafe-eval'");
    expect(directive(csp, 'connect-src')).toContain('ws:');
    expect(directive(csp, 'upgrade-insecure-requests')).toBeUndefined();
  });

  it('reads the origin of a tile template, turning a subdomain placeholder into a wildcard', () => {
    expect(tileOrigin('https://{s}.tiles.example.org/{z}/{x}/{y}.png')).toBe('https://*.tiles.example.org');
    expect(tileOrigin('http://localhost:8081/tiles/{z}/{x}/{y}.png')).toBe('http://localhost:8081');
    expect(tileOrigin('/tiles/{z}/{x}/{y}.png')).toBeNull();
    expect(tileOrigin("https://evil.org;script-src 'unsafe-inline'/x")).toBeNull();
  });

  it('creates a different 128-bit nonce each time', () => {
    const a = createNonce();
    expect(atob(a)).toHaveLength(16);
    expect(createNonce()).not.toBe(a);
  });
});
