import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { DiscoveryShopCard } from '@/components/discovery/ShopCards';
import { JsonLd } from '@/components/seo/JsonLd';
import { PublicShell } from '@/components/shell/PublicShell';
import { Breadcrumb, Pagination } from '@/components/ui/data';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { cn } from '@/lib/cn';
import { dataOrNull, getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { breadcrumbLd } from '@/lib/seo/jsonld';
import { absoluteUrl, localizedAlternates, OG_IMAGE, OG_LOCALE } from '@/lib/seo/site';

const PAGE_SIZE = 24;

type Search = { city?: string | string[]; page?: string | string[] };

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);

function listingPath(city: string | null, page = 1): string {
  const query = new URLSearchParams();
  if (city) query.set('city', city);
  if (page > 1) query.set('page', String(page));
  const text = query.toString();
  return text ? `/shops?${text}` : '/shops';
}

export async function generateMetadata({
  params,
  searchParams,
}: PageProps<'/[locale]/shops'>): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const search = (await searchParams) as Search;
  const city = first(search.city)?.trim() || null;
  const page = Math.max(1, Number(first(search.page)) || 1);
  const t = await getTranslations({ locale, namespace: 'shopsList.meta' });
  const title = city ? t('titleCity', { city }) : t('title');
  return {
    title,
    description: t('description'),
    alternates: localizedAlternates(listingPath(city, page), locale),
    openGraph: {
      type: 'website',
      title,
      description: t('description'),
      locale: OG_LOCALE[locale],
      url: `/${locale}${listingPath(city, page)}`,
      images: [OG_IMAGE],
    },
  };
}

/**
 * Indexable shop listing (DV-A27): every shop listed in discovery, by city, highest rated first, paged. Unlike the
 * personalised `/search`, it does not depend on the visitor's location, so search engines can crawl it.
 */
export default async function ShopsListingPage({ params, searchParams }: PageProps<'/[locale]/shops'>) {
  const locale = asLocale((await params).locale);
  const search = (await searchParams) as Search;
  const city = first(search.city)?.trim() || null;
  const page = Math.max(1, Math.floor(Number(first(search.page)) || 1));
  const t = await getTranslations({ locale, namespace: 'shopsList' });
  const tCrumb = await getTranslations({ locale, namespace: 'shopPage.breadcrumb' });
  const api = await getPublicApi();
  const [areas, result] = await Promise.all([
    optional(() => api.GET('/api/v1/public/areas'), 'areas'),
    api
      .GET('/api/v1/public/shops/search', {
        params: { query: { city: city ?? undefined, sort: 'Rating', page, pageSize: PAGE_SIZE } },
      })
      .then((r) => dataOrNull(r, 'search')),
  ]);
  const cities = [...new Set((areas?.areas ?? []).map((a) => a.city))].sort((a, b) =>
    a.localeCompare(b, locale),
  );
  const title = city ? t('titleCity', { city }) : t('title');

  return (
    <PublicShell variant="marketing">
      <JsonLd
        data={breadcrumbLd([
          { name: tCrumb('home'), url: absoluteUrl(`/${locale}`) },
          { name: tCrumb('shops'), url: absoluteUrl(`/${locale}/shops`) },
        ])}
      />
      <div className="mx-auto flex max-w-[1180px] flex-col gap-6 px-4 py-8 md:px-6 md:py-10">
        <Breadcrumb items={[{ label: tCrumb('home'), href: '/' }, { label: tCrumb('shops') }]} />
        <header className="flex flex-col gap-2">
          <h1 className="text-h1 font-bold text-navy-900">{title}</h1>
          <p className="text-body text-text-secondary">{t('intro')}</p>
          {result && (
            <p className="text-helper font-semibold text-text-strong">
              {t('count', { count: result.total })}
            </p>
          )}
        </header>
        {cities.length > 1 && (
          <nav aria-label={t('cities')}>
            <ul className="flex flex-wrap gap-2">
              {[null, ...cities].map((option) => {
                const active = option === city;
                return (
                  <li key={option ?? 'all'}>
                    <Link
                      href={listingPath(option)}
                      aria-current={active ? 'page' : undefined}
                      className={cn(
                        'inline-flex min-h-11 items-center rounded-pill border px-4 text-label font-bold',
                        active
                          ? 'border-navy-900 bg-navy-900 text-on-navy'
                          : 'border-border bg-surface text-text-strong hover:border-brand-500',
                      )}
                    >
                      {option ?? t('allCities')}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </nav>
        )}
        {result && result.items.length > 0 ? (
          <>
            <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {result.items.map((shop) => (
                <li key={shop.id}>
                  <DiscoveryShopCard shop={shop} headingLevel={2} />
                </li>
              ))}
            </ul>
            {result.total > PAGE_SIZE && (
              <Pagination
                page={page}
                pageSize={PAGE_SIZE}
                total={result.total}
                hrefForPage={(p) => listingPath(city, p)}
              />
            )}
          </>
        ) : (
          <EmptyState icon="store" title={t('empty')} />
        )}
      </div>
    </PublicShell>
  );
}
