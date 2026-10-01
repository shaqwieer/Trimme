import 'server-only';
import { getPublicApi } from '@/lib/api/public';
import type { SitemapPage } from './sitemap';

/**
 * The indexable pages: the landing page, the shop listing, the legal pages, and every shop listed in discovery with its
 * active professionals. If the API cannot be reached the static pages are still listed.
 */
export async function sitemapPages(): Promise<SitemapPage[]> {
  const pages: SitemapPage[] = [{ path: '/' }, { path: '/shops' }, { path: '/terms' }, { path: '/privacy' }];
  try {
    const api = await getPublicApi();
    const { data } = await api.GET('/api/v1/public/sitemap');
    for (const shop of data?.shops ?? []) {
      pages.push({ path: `/shops/${shop.slug}`, lastModified: shop.updatedAt });
      for (const professional of shop.professionalSlugs) {
        pages.push({
          path: `/shops/${shop.slug}/professionals/${professional}`,
          lastModified: shop.updatedAt,
        });
      }
    }
  } catch (error) {
    console.error('Sitemap: the API could not be reached', error instanceof Error ? error.message : error);
  }
  return pages;
}
