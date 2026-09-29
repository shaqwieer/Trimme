import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LocationSheetButton } from '@/components/discovery/LocationSheetButton';
import { ShopListCard } from '@/components/discovery/ShopCards';
import { CustomerShell } from '@/components/shell/CustomerShell';
import { Avatar } from '@/components/ui/Avatar';
import { ButtonLink } from '@/components/ui/Button';
import { type DesignIconName, Icon } from '@/components/ui/icons';
import { RatingStars } from '@/components/ui/Rating';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { readLocation } from '@/lib/discovery/location.server';
import { DEFAULT_RADIUS_KM, WIDE_RADIUS_KM } from '@/lib/discovery/search';
import { formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { NO_INDEX } from '@/lib/seo/site';

export async function generateMetadata({ params }: PageProps<'/[locale]/discover'>): Promise<Metadata> {
  const t = await getTranslations({ locale: asLocale((await params).locale), namespace: 'discover.meta' });
  return { title: t('title'), robots: NO_INDEX };
}

const KNOWN_ICONS = new Set<string>([
  'scissors',
  'user',
  'users',
  'star',
  'heart',
  'layers',
  'tag',
  'coffee',
]);

/**
 * The customer home (c-home "HOME · 390px" 995–1072). Personalised by the location chosen on this device, so never
 * indexed (the landing page is the SEO page). Popular services are category tiles with the lowest nearby price
 * (DV-S11), never a platform-wide price.
 */
export default async function DiscoverPage({ params }: PageProps<'/[locale]/discover'>) {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'discover' });
  const location = await readLocation();
  const api = await getPublicApi();
  const near = location ? { lat: location.lat, lng: location.lng, radiusKm: DEFAULT_RADIUS_KM } : {};

  const [areas, categories, shops, popular, pros] = await Promise.all([
    optional(() => api.GET('/api/v1/public/areas'), 'areas'),
    optional(() => api.GET('/api/v1/public/service-categories'), 'categories'),
    optional(
      () =>
        api.GET('/api/v1/public/shops/search', {
          params: { query: { ...near, sort: location ? 'Distance' : 'Rating', pageSize: 5 } },
        }),
      'nearby shops',
    ),
    optional(
      () => api.GET('/api/v1/public/categories/popular', { params: { query: near } }),
      'popular categories',
    ),
    optional(
      () => api.GET('/api/v1/public/professionals/top', { params: { query: { ...near, limit: 6 } } }),
      'top professionals',
    ),
  ]);
  const categoryById = new Map((categories ?? []).map((c) => [c.id, c]));
  const tiles = (popular ?? []).filter((p) => categoryById.has(p.categoryId)).slice(0, 4);
  const label = location ? location.label || t('currentLocation') : null;

  return (
    <CustomerShell>
      <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-5 md:px-6">
        <header className="flex flex-col gap-4">
          <LocationSheetButton label={label} areas={areas?.areas ?? []} />
          <form action={`/${locale}/search`} method="get" role="search" aria-label={t('search.label')}>
            <label className="flex min-h-12 items-center gap-2 rounded-field bg-bg-subtle px-3.5">
              <Icon name="search" className="size-[18px] text-text-tertiary" />
              <span className="sr-only">{t('search.label')}</span>
              <input
                name="q"
                type="search"
                placeholder={t('search.placeholder')}
                className="min-h-12 w-full bg-transparent text-input text-text-primary outline-none placeholder:text-text-placeholder"
              />
            </label>
          </form>
        </header>

        {categories && categories.length > 0 && (
          <nav aria-label={t('categories')} className="-mx-4 overflow-x-auto px-4">
            <ul className="flex w-max gap-2">
              <li>
                <Link
                  href="/search"
                  className="inline-flex min-h-11 items-center rounded-pill bg-navy-900 px-4 text-label font-bold text-on-navy"
                >
                  {t('all')}
                </Link>
              </li>
              {categories.map((category) => (
                <li key={category.id}>
                  <Link
                    href={{ pathname: '/search', query: { category: category.id } }}
                    className="inline-flex min-h-11 items-center rounded-pill border border-border bg-surface px-4 text-label font-bold text-text-strong hover:border-brand-500"
                  >
                    {localizedName(locale, category.nameAr, category.nameEn)}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        )}

        <section aria-labelledby="near-heading" className="flex flex-col gap-3">
          <div className="flex items-center justify-between gap-3">
            <h2 id="near-heading" className="text-h3 font-bold text-navy-900">
              {location ? t('nearYou') : t('topRated')}
            </h2>
            <Link
              href={{ pathname: '/search', query: { view: 'map' } }}
              className="inline-flex min-h-11 items-center text-label font-bold text-text-link hover:underline"
            >
              {t('map')}
            </Link>
          </div>
          {!location && (
            <div className="flex flex-col gap-3 rounded-card border border-brand-200 bg-brand-50 p-4">
              <p className="text-label font-bold text-navy-900">{t('noLocation.title')}</p>
              <p className="text-helper text-text-secondary">{t('noLocation.body')}</p>
              <ButtonLink
                href={{ pathname: '/onboarding/location', query: { returnTo: '/discover' } }}
                variant="primary"
                size="sm"
                icon="pin"
                className="self-start"
              >
                {t('noLocation.cta')}
              </ButtonLink>
            </div>
          )}
          {shops && shops.items.length > 0 ? (
            <ul className="flex flex-col gap-3">
              {shops.items.map((shop) => (
                <li key={shop.id}>
                  <ShopListCard shop={shop} />
                </li>
              ))}
            </ul>
          ) : (
            <EmptyState
              icon="store"
              title={t('nearEmpty.title')}
              body={t('nearEmpty.body', { km: DEFAULT_RADIUS_KM })}
              action={
                <ButtonLink
                  href={{ pathname: '/search', query: { radius: WIDE_RADIUS_KM } }}
                  variant="outline"
                  size="sm"
                >
                  {t('nearEmpty.cta', { km: WIDE_RADIUS_KM })}
                </ButtonLink>
              }
            />
          )}
        </section>

        {tiles.length > 0 && (
          <section aria-labelledby="popular-heading" className="flex flex-col gap-3">
            <h2 id="popular-heading" className="text-h3 font-bold text-navy-900">
              {t('popular.title')}
            </h2>
            <ul className="grid grid-cols-2 gap-3">
              {tiles.map((tile) => {
                const category = categoryById.get(tile.categoryId)!;
                const icon = (KNOWN_ICONS.has(category.icon) ? category.icon : 'scissors') as DesignIconName;
                return (
                  <li key={tile.categoryId}>
                    <Link
                      href={{ pathname: '/search', query: { category: tile.categoryId } }}
                      className="flex min-h-11 flex-col gap-2 rounded-card border border-border bg-surface p-3.5 shadow-e1 hover:shadow-e2"
                    >
                      <span className="flex size-9 items-center justify-center rounded-field bg-brand-100 text-brand-700">
                        <Icon name={icon} className="size-[18px]" />
                      </span>
                      <span className="text-label font-bold text-text-primary">
                        {localizedName(locale, category.nameAr, category.nameEn)}
                      </span>
                      <span className="text-helper text-text-secondary">
                        {t('popular.from', { price: formatPrice(tile.minPrice, locale, tile.currency) })}
                        {' · '}
                        {t('popular.shops', { count: tile.shopCount })}
                      </span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          </section>
        )}

        {pros && pros.length > 0 && (
          <section aria-labelledby="pros-heading" className="flex flex-col gap-3">
            <h2 id="pros-heading" className="text-h3 font-bold text-navy-900">
              {t('topPros.title')}
            </h2>
            <ul className="grid grid-cols-3 gap-3">
              {pros.map((pro) => {
                const name = localizedName(locale, pro.nameAr, pro.nameEn);
                return (
                  <li key={pro.id}>
                    <Link
                      href={`/shops/${pro.shopSlug}/professionals/${pro.slug}`}
                      className="flex min-h-11 flex-col items-center gap-1.5 rounded-card border border-border bg-surface p-3 text-center shadow-e1 hover:shadow-e2"
                    >
                      <Avatar name={name} src={pro.avatarUrl} size="lg" />
                      <span className="text-label font-bold text-text-primary">{name.split(' ')[0]}</span>
                      {(pro.specialtyAr || pro.specialtyEn) && (
                        <span className="line-clamp-1 text-helper text-text-secondary">
                          {localizedName(locale, pro.specialtyAr ?? '', pro.specialtyEn)}
                        </span>
                      )}
                      <RatingStars value={pro.rating} />
                    </Link>
                  </li>
                );
              })}
            </ul>
          </section>
        )}
      </div>
    </CustomerShell>
  );
}
