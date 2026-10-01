import { sitemapFiles, sitemapIndexXml, xmlResponse } from '@/lib/seo/sitemap';

/** Per request: the URLs use the deployed origin (TRIMME_SITE_URL is runtime configuration). */
export const dynamic = 'force-dynamic';

/** `/sitemap.xml`: the index of the per-locale sitemaps (R-WEB-10). robots.txt points here. */
export function GET() {
  return xmlResponse(sitemapIndexXml(sitemapFiles()));
}
