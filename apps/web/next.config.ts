import path from 'node:path';
import type { NextConfig } from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

const withNextIntl = createNextIntlPlugin('./src/i18n/request.ts');

const nextConfig: NextConfig = {
  output: 'standalone',
  // Monorepo: trace server files from the repository root so the standalone bundle is self-contained.
  outputFileTracingRoot: path.join(import.meta.dirname, '../../'),
  poweredByHeader: false,
  reactStrictMode: true,
  typedRoutes: false,
  images: {
    formats: ['image/avif', 'image/webp'],
    qualities: [75, 90],
  },
  /**
   * Same-origin API access. In production Nginx routes /api and /hubs to the API before Next.js sees them;
   * in development (and a bare `next start`) these rewrites forward them. The destination is fixed at build time.
   */
  async rewrites() {
    const api = process.env.TRIMME_API_INTERNAL_URL ?? 'http://localhost:8080';
    return [
      { source: '/api/:path*', destination: `${api}/api/:path*` },
      { source: '/hubs/:path*', destination: `${api}/hubs/:path*` },
    ];
  },
  async headers() {
    return [
      {
        source: '/:path*',
        headers: [
          { key: 'X-Content-Type-Options', value: 'nosniff' },
          { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
          { key: 'X-Frame-Options', value: 'DENY' },
          { key: 'Permissions-Policy', value: 'camera=(), microphone=(), payment=(), geolocation=(self)' },
          // Isolates the window from cross-origin openers. No COEP: it would block the map's cross-origin tiles.
          { key: 'Cross-Origin-Opener-Policy', value: 'same-origin' },
          { key: 'Cross-Origin-Resource-Policy', value: 'same-origin' },
        ],
      },
      {
        // Brand images (logo, share image, hero) are not content-hashed but change rarely (D-121).
        source: '/brand/:file*',
        headers: [{ key: 'Cache-Control', value: 'public, max-age=86400, stale-while-revalidate=604800' }],
      },
      {
        // Pages get a per-request nonce policy from the proxy (D-117); paths with a file extension skip the proxy, so
        // files, and the 404 page for a missing file, get a fixed policy that runs no script.
        source: '/:file((?!_next/).*\.[^/]*)',
        headers: [
          {
            key: 'Content-Security-Policy',
            value:
              "default-src 'self'; script-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
          },
        ],
      },
    ];
  },
};

export default withNextIntl(nextConfig);
