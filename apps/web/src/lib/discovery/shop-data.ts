import 'server-only';
import { cache } from 'react';
import { dataOrNull, getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';

/**
 * Public shop reads for one request. `cache` shares a call between `generateMetadata` and the page (and between the
 * page's islands), while each request still asks the API, whose public cache keeps them cheap (D-093).
 */
export const getPublicShop = cache(async (slug: string) => {
  const api = await getPublicApi();
  return dataOrNull(await api.GET('/api/v1/public/shops/{slug}', { params: { path: { slug } } }), 'shop');
});

/**
 * The live part (open now, booking state, professionals' next times); never cached anywhere. It is secondary, so any
 * failure (rate limit, API error) yields `null` and the page renders without it instead of failing.
 */
export const getPublicShopStatus = cache(async (slug: string) => {
  const api = await getPublicApi();
  return optional(
    () => api.GET('/api/v1/public/shops/{slug}/status', { params: { path: { slug } } }),
    'shop status',
  );
});

export const getPublicProfessional = cache(async (slug: string, professionalSlug: string) => {
  const api = await getPublicApi();
  return dataOrNull(
    await api.GET('/api/v1/public/shops/{slug}/professionals/{professionalSlug}', {
      params: { path: { slug, professionalSlug } },
    }),
    'professional',
  );
});

export { bookHref, directionsUrl } from '@/lib/booking/links';
