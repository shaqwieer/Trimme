import type { MetadataRoute } from 'next';
import { routing } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { absoluteUrl } from '@/lib/seo/site';

/** Built per request from the API (never at build time, when there is no API), so new shops appear at once. */
export const dynamic = 'force-dynamic';

function entry(path: string, lastModified?: string): MetadataRoute.Sitemap[number] {
  const suffix = path === '/' ? '' : path;
  return {
    url: absoluteUrl(`/${routing.defaultLocale}${suffix}`),
    lastModified,
    alternates: {
      languages: Object.fromEntries(
        routing.locales.map((locale) => [locale, absoluteUrl(`/${locale}${suffix}`)]),
      ),
    },
  };
}

/**
 * sitemap.xml (R-WEB-10): the landing page, the shop listing, the legal pages, and every shop listed in discovery with
 * its active professionals, each with its Arabic and English alternates. If the API cannot be reached the static pages
 * are still listed.
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const pages = [entry('/'), entry('/shops'), entry('/terms'), entry('/privacy')];
  try {
    const api = await getPublicApi();
    const { data } = await api.GET('/api/v1/public/sitemap');
    for (const shop of data?.shops ?? []) {
      pages.push(entry(`/shops/${shop.slug}`, shop.updatedAt));
      for (const professional of shop.professionalSlugs) {
        pages.push(entry(`/shops/${shop.slug}/professionals/${professional}`, shop.updatedAt));
      }
    }
  } catch (error) {
    console.error('Sitemap: the API could not be reached', error instanceof Error ? error.message : error);
  }
  return pages;
}
