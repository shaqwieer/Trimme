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

/** A booking link for the wizard (Phase 12, D-028): the step state lives in the URL. */
export function bookHref(slug: string, query: Record<string, string | undefined> = {}): string {
  const params = new URLSearchParams(
    Object.entries(query).filter((entry): entry is [string, string] => Boolean(entry[1])),
  );
  const text = params.toString();
  return text ? `/shops/${slug}/book?${text}` : `/shops/${slug}/book`;
}

/** OpenStreetMap directions to the shop (D-007: OSM everywhere; opens the visitor's route in a new tab). */
export function directionsUrl(latitude: number, longitude: number): string {
  return `https://www.openstreetmap.org/directions?to=${latitude.toFixed(6)}%2C${longitude.toFixed(6)}`;
}
