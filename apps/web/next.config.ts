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
        ],
      },
    ];
  },
};

export default withNextIntl(nextConfig);
