import type { Metadata } from 'next';
import { cookies } from 'next/headers';
import { getTranslations } from 'next-intl/server';
import { rebookHref } from '@/components/booking/BookingCards';
import { RebookNotice } from '@/components/booking/RebookNotice';
import { Logo } from '@/components/brand/Logo';
import { SaudiBadge } from '@/components/brand/SaudiBadge';
import { LocationSheetButton } from '@/components/discovery/LocationSheetButton';
import { ShopListCard } from '@/components/discovery/ShopCards';
import { CustomerShell } from '@/components/shell/CustomerShell';
import { ButtonLink } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { getServerApi } from '@/lib/api/server';
import { getMe } from '@/lib/auth/server';
import { parseRebookDismissed, REBOOK_DISMISSED_COOKIE } from '@/lib/booking/rebookNotice';
import { readLocation } from '@/lib/discovery/location.server';
import { DEFAULT_RADIUS_KM, WIDE_RADIUS_KM } from '@/lib/discovery/search';
import { localizedName } from '@/lib/i18n/localized';
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

  const [areas, shops, lastVisit] = await Promise.all([
    optional(() => api.GET('/api/v1/public/areas'), 'areas'),
    optional(
      () =>
        api.GET('/api/v1/public/shops/search', {
          params: { query: { ...near, sort: location ? 'Distance' : 'Rating', pageSize: 10 } },
        }),
      'salons',
    ),
    lastCompletedVisit(),
  ]);
  const label = location ? location.label || t('currentLocation') : null;

  return (
    <CustomerShell>
      <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6">
        <header className="flex flex-col items-center gap-4 text-center">
          <Logo height={72} priority />
          <SaudiBadge />
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
        {lastVisit && (
          <RebookNotice
            bookingId={lastVisit.id}
            shop={localizedName(locale, lastVisit.shop.nameAr, lastVisit.shop.nameEn)}
            href={rebookHref(lastVisit)}
          />
        )}

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

/**
 * A signed-in customer's last completed visit, for the corner «أعد الحجز» notice (D-155, the client's 5-4; a notice
 * since D-156). Null for a guest, a staff account, no completed visit yet, or when the list cannot be read (the home
 * page never fails on it). Also null while the customer has an upcoming booking, since D-155 allows only one and the
 * new booking would be refused, and once its ✕ closed the notice for this visit.
 */
async function lastCompletedVisit() {
  const me = await getMe();
  if (me?.userType !== 'Customer') return null;
  const api = await getServerApi();
  const [past, active] = await Promise.all([
    optional(
      () => api.GET('/api/v1/me/bookings', { params: { query: { tab: 'Past', page: 1, pageSize: 10 } } }),
      'last visit',
    ),
    optional(() => api.GET('/api/v1/me/bookings/active'), 'active booking'),
  ]);
  if (active) return null;
  const visit = past?.items.find((b) => b.status === 'Completed');
  const dismissed = parseRebookDismissed((await cookies()).get(REBOOK_DISMISSED_COOKIE)?.value);
  return visit && visit.id !== dismissed ? visit : null;
}
