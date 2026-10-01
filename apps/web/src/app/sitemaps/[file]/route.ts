import { hasLocale } from 'next-intl';
import { routing } from '@/i18n/routing';
import { localeSitemapXml, xmlResponse } from '@/lib/seo/sitemap';
import { sitemapPages } from '@/lib/seo/sitemapPages';

/** Built per request from the API (never at build time, when there is no API), so new shops appear at once. */
export const dynamic = 'force-dynamic';

/** `/sitemaps/ar.xml`, `/sitemaps/en.xml`: one locale's indexable pages with their alternates (R-WEB-10). */
export async function GET(_request: Request, { params }: RouteContext<'/sitemaps/[file]'>) {
  const locale = (await params).file.replace(/\.xml$/, '');
  if (!hasLocale(routing.locales, locale)) {
    return new Response(null, { status: 404 });
  }
  return xmlResponse(localeSitemapXml(locale, await sitemapPages()));
}
