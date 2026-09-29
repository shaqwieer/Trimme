import type { MetadataRoute } from 'next';
import { routing } from '@/i18n/routing';
import { absoluteUrl } from '@/lib/seo/site';

/** Per request: the sitemap URL uses the deployed origin (TRIMME_SITE_URL is runtime configuration). */
export const dynamic = 'force-dynamic';

/**
 * robots.txt (R-WEB-10, R-NEG-10). Private dashboards, auth flows and personalised discovery pages are excluded (they also
 * send `noindex`). Rules are prefix matches, so the shop dashboard is listed as `/xx/shop$` and `/xx/shop/` to keep the
 * public `/xx/shops` pages crawlable.
 */
export default function robots(): MetadataRoute.Robots {
  const privatePaths = routing.locales.flatMap((locale) => [
    `/${locale}/account`,
    `/${locale}/admin`,
    `/${locale}/auth`,
    `/${locale}/dev`,
    `/${locale}/discover`,
    `/${locale}/search`,
    `/${locale}/onboarding`,
    `/${locale}/shop$`,
    `/${locale}/shop/`,
    // The booking wizard (Phase 12) is a private flow reached from the public pages' booking links.
    `/${locale}/shops/*/book`,
  ]);
  return {
    rules: [{ userAgent: '*', allow: '/', disallow: ['/api/', '/hubs/', ...privatePaths] }],
    sitemap: absoluteUrl('/sitemap.xml'),
  };
}
