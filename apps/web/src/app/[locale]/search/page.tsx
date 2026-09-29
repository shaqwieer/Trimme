import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { FilterSheet } from '@/components/discovery/FilterSheet';
import { ResultsMap } from '@/components/discovery/ResultsMap';
import { ShopResultCard } from '@/components/discovery/ShopCards';
import { CustomerShell } from '@/components/shell/CustomerShell';
import { ButtonLink } from '@/components/ui/Button';
import { Pagination } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { cn } from '@/lib/cn';
import { dataOrNull, getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { readLocation } from '@/lib/discovery/location.server';
import {
  activeFilterCount,
  DEFAULT_RADIUS_KM,
  filtersToQuery,
  LIST_PAGE_SIZE,
  MAP_PAGE_SIZE,
  parseSearchFilters,
  type SearchFilters,
  toApiQuery,
  WIDE_RADIUS_KM,
} from '@/lib/discovery/search';
import { formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { NO_INDEX } from '@/lib/seo/site';

export async function generateMetadata({ params }: PageProps<'/[locale]/search'>): Promise<Metadata> {
  const t = await getTranslations({ locale: asLocale((await params).locale), namespace: 'search.meta' });
  return { title: t('title'), robots: NO_INDEX };
}

const chipClass = (active: boolean) =>
  cn(
    'inline-flex min-h-11 items-center gap-1.5 rounded-pill px-4 text-label font-bold whitespace-nowrap',
    active
      ? 'bg-navy-900 text-on-navy'
      : 'border border-border bg-surface text-text-strong hover:border-brand-500',
  );

/**
 * Search results and map (c-home "SEARCH RESULTS" 1074–1118, c-map 1123–1238). Every filter lives in the URL, so the
 * list, the map and the filter drawer share one state (mapRules #3), and back/refresh/share keep it. Personalised by
 * location, so never indexed.
 */
export default async function SearchPage({ params, searchParams }: PageProps<'/[locale]/search'>) {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'search' });
  const location = await readLocation();
  const filters = parseSearchFilters(await searchParams, location !== null);
  const pageSize = filters.view === 'map' ? MAP_PAGE_SIZE : LIST_PAGE_SIZE;
  const api = await getPublicApi();
  const [categories, result] = await Promise.all([
    optional(() => api.GET('/api/v1/public/service-categories'), 'categories'),
    api
      .GET('/api/v1/public/shops/search', { params: { query: toApiQuery(filters, location, pageSize) } })
      .then((r) => dataOrNull(r, 'search')),
  ]);
  const href = (overrides: Partial<SearchFilters>) => ({
    pathname: '/search',
    query: filtersToQuery({ ...filters, page: 1, ...overrides }),
  });
  const radius = filters.radiusKm ?? DEFAULT_RADIUS_KM;
  const active = activeFilterCount(filters);
  const total = result?.total ?? 0;

  return (
    <CustomerShell>
      <div className="mx-auto flex max-w-[960px] flex-col gap-4 px-4 py-4 md:px-6">
        <header className="flex flex-col gap-3">
          <div className="flex items-center gap-2">
            <Link
              href="/discover"
              aria-label={t('back')}
              className="inline-flex size-11 shrink-0 items-center justify-center rounded-field border border-border bg-surface text-text-strong"
            >
              <Icon name="chevL" className="size-5" />
            </Link>
            <form
              action={`/${locale}/search`}
              method="get"
              role="search"
              aria-label={t('label')}
              className="flex-1"
            >
              {Object.entries(filtersToQuery({ ...filters, q: '', page: 1 })).map(([name, value]) => (
                <input key={name} type="hidden" name={name} value={value} />
              ))}
              <label className="flex min-h-11 items-center gap-2 rounded-field bg-bg-subtle px-3.5">
                <Icon name="search" className="size-[18px] text-text-tertiary" />
                <span className="sr-only">{t('label')}</span>
                <input
                  name="q"
                  type="search"
                  defaultValue={filters.q}
                  placeholder={t('placeholder')}
                  className="min-h-11 w-full bg-transparent text-input text-text-primary outline-none placeholder:text-text-placeholder"
                />
              </label>
            </form>
          </div>
          <nav aria-label={t('quick')} className="-mx-4 overflow-x-auto px-4">
            <ul className="flex w-max items-center gap-2">
              <li>
                <FilterSheet
                  filters={filters}
                  categories={(categories ?? []).map((c) => ({
                    id: c.id,
                    name: localizedName(locale, c.nameAr, c.nameEn),
                  }))}
                  priceRange={result?.priceRange ?? null}
                  location={location}
                  activeCount={active}
                />
              </li>
              {location && (
                <li>
                  <Link
                    href={href({ sort: 'nearest' })}
                    aria-current={filters.sort === 'nearest' ? 'true' : undefined}
                    className={chipClass(filters.sort === 'nearest')}
                  >
                    {t('nearest')}
                  </Link>
                </li>
              )}
              <li>
                <Link
                  href={href({ openNow: !filters.openNow })}
                  aria-current={filters.openNow ? 'true' : undefined}
                  className={chipClass(filters.openNow)}
                >
                  {t('openNow')}
                </Link>
              </li>
              <li>
                <Link
                  href={href({ view: filters.view === 'map' ? 'list' : 'map' })}
                  className={chipClass(false)}
                >
                  <Icon name={filters.view === 'map' ? 'list' : 'pin'} className="size-4" />
                  {filters.view === 'map' ? t('showList') : t('showMap')}
                </Link>
              </li>
            </ul>
          </nav>
        </header>

        <p className="text-helper text-text-secondary" aria-live="polite">
          <span className="font-bold text-text-primary">
            {location
              ? t('results', { count: total, km: formatNumber(radius, locale) })
              : t('resultsAll', { count: total })}
          </span>
          {!location && (
            <>
              {' · '}
              {t('noLocation')}{' '}
              <Link
                href={{ pathname: '/onboarding/location', query: { returnTo: '/search' } }}
                className="font-bold text-text-link hover:underline"
              >
                {t('setLocation')}
              </Link>
            </>
          )}
        </p>

        {!result || result.items.length === 0 ? (
          <EmptyState
            icon="search"
            title={t('empty.title')}
            body={t('empty.body')}
            action={
              <div className="flex flex-wrap justify-center gap-2">
                {location && radius < WIDE_RADIUS_KM && (
                  <ButtonLink href={href({ radiusKm: WIDE_RADIUS_KM })} variant="outline" size="sm">
                    {t('empty.widen', { km: WIDE_RADIUS_KM })}
                  </ButtonLink>
                )}
                {active > 0 && (
                  <ButtonLink
                    href={{ pathname: '/search', query: filters.q ? { q: filters.q } : {} }}
                    variant="ghost"
                    size="sm"
                  >
                    {t('empty.clear')}
                  </ButtonLink>
                )}
              </div>
            }
          />
        ) : filters.view === 'map' ? (
          <ResultsMap
            items={result.items}
            origin={location ? { lat: location.lat, lng: location.lng } : null}
          />
        ) : (
          <>
            <ul className="flex flex-col gap-3" aria-label={t('meta.title')}>
              {result.items.map((shop) => (
                <li key={shop.id}>
                  <ShopResultCard shop={shop} />
                </li>
              ))}
            </ul>
            {result.total > pageSize && (
              <Pagination
                page={filters.page}
                pageSize={pageSize}
                total={result.total}
                hrefForPage={(p) =>
                  `/search?${new URLSearchParams(filtersToQuery({ ...filters, page: p })).toString()}`
                }
              />
            )}
          </>
        )}
      </div>
    </CustomerShell>
  );
}
