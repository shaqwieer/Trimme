import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { Logo } from '@/components/brand/Logo';
import { LocationSheetButton } from '@/components/discovery/LocationSheetButton';
import { ShopListCard } from '@/components/discovery/ShopCards';
import { CustomerShell } from '@/components/shell/CustomerShell';
import { ButtonLink } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { readLocation } from '@/lib/discovery/location.server';
import { DEFAULT_RADIUS_KM, WIDE_RADIUS_KM } from '@/lib/discovery/search';
import { NO_INDEX } from '@/lib/seo/site';

export async function generateMetadata({ params }: PageProps<'/[locale]/discover'>): Promise<Metadata> {
  const t = await getTranslations({ locale: asLocale((await params).locale), namespace: 'discover.meta' });
  return { title: t('title'), robots: NO_INDEX };
}

/**
 * The customer home, two parts only so a customer never gets lost (D-130): the TRIMME logo centred with the location
 * right under it ("set your location to see what's nearest", or the chosen place to change), then the salons —
 * nearest first once a location is set, otherwise the top-rated. Personalised by the location chosen on this device, so
 * never indexed (the landing page is the SEO page).
 */
export default async function DiscoverPage({ params }: PageProps<'/[locale]/discover'>) {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'discover' });
  const location = await readLocation();
  const api = await getPublicApi();
  const near = location ? { lat: location.lat, lng: location.lng, radiusKm: DEFAULT_RADIUS_KM } : {};

  const [areas, shops] = await Promise.all([
    optional(() => api.GET('/api/v1/public/areas'), 'areas'),
    optional(
      () =>
        api.GET('/api/v1/public/shops/search', {
          params: { query: { ...near, sort: location ? 'Distance' : 'Rating', pageSize: 10 } },
        }),
      'salons',
    ),
  ]);
  const label = location ? location.label || t('currentLocation') : null;

  return (
    <CustomerShell>
      <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6">
        <header className="flex flex-col items-center gap-4 text-center">
          <Logo height={72} priority />
          {location ? (
            <LocationSheetButton label={label} areas={areas?.areas ?? []} />
          ) : (
            <div className="flex w-full flex-col items-center gap-2 rounded-card border border-brand-200 bg-brand-50 p-4">
              <p className="text-label font-bold text-navy-900">{t('noLocation.title')}</p>
              <p className="text-helper text-text-secondary">{t('noLocation.body')}</p>
              <ButtonLink
                href={{ pathname: '/onboarding/location', query: { returnTo: '/discover' } }}
                variant="primary"
                size="md"
                icon="pin"
              >
                {t('noLocation.cta')}
              </ButtonLink>
            </div>
          )}
        </header>

        <section aria-labelledby="salons-heading" className="flex flex-col gap-3">
          <div className="flex items-center justify-between gap-3">
            <h2 id="salons-heading" className="text-h3 font-bold text-navy-900">
              {location ? t('nearYou') : t('topRatedSalons')}
            </h2>
            <Link
              href="/shops"
              className="inline-flex min-h-11 items-center text-label font-bold text-text-link hover:underline"
            >
              {t('allSalons')}
            </Link>
          </div>
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
      </div>
    </CustomerShell>
  );
}
