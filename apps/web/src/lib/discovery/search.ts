import type { ChosenLocation } from './location';

/**
 * Discovery filters as they appear in the page URL (`/search?q=&category=&sort=&open=1…`), so back, refresh and shared
 * links keep the search (the filter drawer and the map share this one state, mapRules #3).
 */
export type SearchFilters = {
  q: string;
  category: string | null;
  sort: SearchSort;
  openNow: boolean;
  verified: boolean;
  today: boolean;
  minPrice: number | null;
  maxPrice: number | null;
  radiusKm: number | null;
  view: 'list' | 'map';
  page: number;
};

export type SearchSort = 'nearest' | 'rating' | 'earliest';

/** The API's sort names (`DiscoverySort`). */
export const API_SORT: Record<SearchSort, 'Distance' | 'Rating' | 'Earliest'> = {
  nearest: 'Distance',
  rating: 'Rating',
  earliest: 'Earliest',
};

export const DEFAULT_RADIUS_KM = 10;
export const WIDE_RADIUS_KM = 25;
export const MAP_PAGE_SIZE = 50;
export const LIST_PAGE_SIZE = 20;

type Params = Record<string, string | string[] | undefined>;

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function positive(value: string | undefined): number | null {
  if (value === undefined || value === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

/** Reads the filters from search params, ignoring anything malformed. */
export function parseSearchFilters(params: Params, hasLocation: boolean): SearchFilters {
  const sort = first(params.sort);
  const radius = positive(first(params.radius));
  return {
    q: (first(params.q) ?? '').trim().slice(0, 100),
    category: UUID.test(first(params.category) ?? '') ? first(params.category)! : null,
    sort: sort === 'rating' || sort === 'earliest' ? sort : hasLocation ? 'nearest' : 'rating',
    openNow: first(params.open) === '1',
    verified: first(params.verified) === '1',
    today: first(params.today) === '1',
    minPrice: positive(first(params.min)),
    maxPrice: positive(first(params.max)),
    radiusKm: radius !== null && radius > 0 && radius <= 50 ? radius : null,
    view: first(params.view) === 'map' ? 'map' : 'list',
    page: Math.max(1, Math.floor(positive(first(params.page)) ?? 1)),
  };
}

/** The number shown on the "Filters · N" chip: every filter the drawer sets that differs from the default. */
export function activeFilterCount(filters: SearchFilters): number {
  return [
    filters.category !== null,
    filters.openNow,
    filters.verified,
    filters.today,
    filters.minPrice !== null || filters.maxPrice !== null,
    filters.sort !== 'nearest' && filters.sort !== 'rating',
  ].filter(Boolean).length;
}

/** The page URL's query for these filters; defaults are left out so URLs stay short. */
export function filtersToQuery(filters: Partial<SearchFilters>): Record<string, string> {
  const query: Record<string, string> = {};
  if (filters.q) query.q = filters.q;
  if (filters.category) query.category = filters.category;
  if (filters.sort && filters.sort !== 'nearest') query.sort = filters.sort;
  if (filters.openNow) query.open = '1';
  if (filters.verified) query.verified = '1';
  if (filters.today) query.today = '1';
  if (filters.minPrice != null) query.min = String(filters.minPrice);
  if (filters.maxPrice != null) query.max = String(filters.maxPrice);
  if (filters.radiusKm != null) query.radius = String(filters.radiusKm);
  if (filters.view === 'map') query.view = 'map';
  if (filters.page && filters.page > 1) query.page = String(filters.page);
  return query;
}

/** The API query (`GET /public/shops/search`) for these filters and location. */
export function toApiQuery(filters: SearchFilters, location: ChosenLocation | null, pageSize: number) {
  return {
    lat: location?.lat,
    lng: location?.lng,
    radiusKm: location ? (filters.radiusKm ?? DEFAULT_RADIUS_KM) : undefined,
    q: filters.q || undefined,
    categoryId: filters.category ?? undefined,
    openNow: filters.openNow || undefined,
    verified: filters.verified || undefined,
    bookableToday: filters.today || undefined,
    minPrice: filters.minPrice ?? undefined,
    maxPrice: filters.maxPrice ?? undefined,
    sort: API_SORT[filters.sort === 'nearest' && !location ? 'rating' : filters.sort],
    page: filters.view === 'map' ? 1 : filters.page,
    pageSize,
  };
}
