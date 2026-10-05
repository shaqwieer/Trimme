import type { Metadata } from 'next';
import Image from 'next/image';
import { notFound } from 'next/navigation';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { FavoriteButton } from '@/components/favorites/FavoriteButton';
import { JsonLd } from '@/components/seo/JsonLd';
import { PublicShell } from '@/components/shell/PublicShell';
import { ShopDistance } from '@/components/shops/public/ShopDistance';
import {
  AboutPanel,
  NextTime,
  ProfessionalsPanel,
  ReviewsPanel,
  ServicesPanel,
} from '@/components/shops/public/ShopPanels';
import { ShopTabs } from '@/components/shops/public/ShopTabs';
import { ImagePlaceholder } from '@/components/ui/Avatar';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { Breadcrumb } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { RatingStars } from '@/components/ui/Rating';
import { InlineAlert, Skeleton } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { mayBeSignedIn } from '@/lib/auth/session.server';
import { optional } from '@/lib/api/safe';
import { openingLabel } from '@/lib/discovery/opening';
import { bookHref, directionsUrl, getPublicShop, getPublicShopStatus } from '@/lib/discovery/shop-data';
import { type AppLocale, formatTime } from '@/lib/i18n/format';
import { aggregateRatingLd, breadcrumbLd, openingHoursLd } from '@/lib/seo/jsonld';
import { absoluteUrl, localizedAlternates, NO_INDEX, OG_IMAGE, OG_LOCALE } from '@/lib/seo/site';

const TABS = ['services', 'professionals', 'reviews', 'about'] as const;

function shopName(shop: { nameAr: string; nameEn: string }, locale: AppLocale) {
  return locale === 'en' ? shop.nameEn || shop.nameAr : shop.nameAr;
}

const separator = (locale: AppLocale) => (locale === 'ar' ? '، ' : ', ');

function area(
  shop: { location?: { district?: string | null; city?: string | null } | null },
  locale: AppLocale,
) {
  return [shop.location?.district, shop.location?.city].filter(Boolean).join(separator(locale));
}

export async function generateMetadata({ params }: PageProps<'/[locale]/shops/[slug]'>): Promise<Metadata> {
  const { locale: raw, slug } = await params;
  const locale = asLocale(raw);
  const shop = await getPublicShop(slug);
  if (!shop) return {};
  const t = await getTranslations({ locale, namespace: 'shopPage.meta' });
  const name = shopName(shop, locale);
  const where = area(shop, locale);
  const description = where ? t('description', { name, area: where }) : t('descriptionNoArea', { name });
  const image = shop.coverUrl ?? shop.logoUrl ?? OG_IMAGE.url;
  return {
    title: name,
    description,
    alternates: localizedAlternates(`/shops/${shop.slug}`, locale),
    robots: shop.listedInDiscovery ? undefined : NO_INDEX,
    openGraph: {
      type: 'website',
      title: name,
      description,
      locale: OG_LOCALE[locale],
      url: `/${locale}/shops/${shop.slug}`,
      images: [{ url: image }],
    },
  };
}

/** Open now / closes at, and the paused or not-listed notice: the live part of the page, never cached (D-093). */
async function LiveStatus({ slug, locale, timeZone }: { slug: string; locale: AppLocale; timeZone: string }) {
  const [status, t, tPage] = await Promise.all([
    getPublicShopStatus(slug),
    getTranslations({ locale, namespace: 'opening' }),
    getTranslations({ locale, namespace: 'shopPage' }),
  ]);
  if (!status) return null;
  return (
    <div className="flex flex-col gap-3">
      <p className="flex flex-wrap items-center gap-2" data-testid="shop-open-status">
        <Badge tone={status.isOpenNow ? 'success' : 'neutral'}>
          {status.isOpenNow ? t('openNow') : t('closed')}
        </Badge>
        <span className="text-helper text-text-secondary">
          {status.isOpenNow && status.closesAt
            ? t('closesAt', { time: formatTime(status.closesAt, locale, timeZone) })
            : status.nextOpensAt
              ? openingLabel(t, locale, status, timeZone)
              : null}
        </span>
      </p>
      {!status.acceptsOnlineBookings && (
        <InlineAlert
          tone="warning"
          title={status.blockedReason === 'shop.paused' ? tPage('paused.title') : tPage('notListed.title')}
        >
          {status.blockedReason === 'shop.paused' ? tPage('paused.body') : tPage('notListed.body')}
        </InlineAlert>
      )}
    </div>
  );
}

/**
 * The sticky «احجز الآن» bar (c-shop 1358–1361), centred and without a "starts from" price (D-128): the prices are on
 * the services themselves. Disabled with the reason while online booking is off.
 */
async function BookBar({ slug, locale }: { slug: string; locale: AppLocale }) {
  const [status, t] = await Promise.all([
    getPublicShopStatus(slug),
    getTranslations({ locale, namespace: 'shopPage.footer' }),
  ]);
  const accepts = status?.acceptsOnlineBookings ?? false;
  return (
    <div className="sticky bottom-0 z-10 border-t border-border bg-surface/95 backdrop-blur-md">
      <div className="mx-auto flex max-w-[960px] items-center justify-center px-4 py-3 md:px-6">
        {accepts ? (
          <ButtonLink href={bookHref(slug)} variant="primary" size="lg" className="w-full max-w-[420px]">
            {t('book')}
          </ButtonLink>
        ) : (
          <span className="text-helper font-bold text-text-secondary">{t('unavailable')}</span>
        )}
      </div>
    </div>
  );
}

/**
 * The public shop page (c-shop 1243–1363, DV-A23): cover and gallery, identity with the verified mark, rating, live open
 * status, address with directions, tabs for services and packages, barbers, reviews and about (hours, policies, map),
 * and the sticky booking bar. Indexable with LocalBusiness and BreadcrumbList data while the shop is listed.
 */
export default async function ShopPage({ params, searchParams }: PageProps<'/[locale]/shops/[slug]'>) {
  const { locale: raw, slug } = await params;
  const locale = asLocale(raw);
  const query = await searchParams;
  const shop = await getPublicShop(slug);
  if (!shop) notFound();

  const tab = typeof query.tab === 'string' ? query.tab : 'services';
  const reviewsPage = Math.max(1, Number(typeof query.reviewsPage === 'string' ? query.reviewsPage : 1) || 1);
  const t = await getTranslations({ locale, namespace: 'shopPage' });
  const api = await getPublicApi();
  const signedIn = await mayBeSignedIn();
  const path = { params: { path: { slug: shop.slug } } };
  const [services, packages, professionals, reviews] = await Promise.all([
    optional(() => api.GET('/api/v1/public/shops/{slug}/services', path), 'services'),
    optional(() => api.GET('/api/v1/public/shops/{slug}/packages', path), 'packages'),
    optional(() => api.GET('/api/v1/public/shops/{slug}/professionals', path), 'professionals'),
    optional(
      () =>
        api.GET('/api/v1/public/shops/{slug}/reviews', {
          params: { path: { slug: shop.slug }, query: { page: reviewsPage, pageSize: 10 } },
        }),
      'reviews',
    ),
  ]);

  const name = shopName(shop, locale);
  const url = absoluteUrl(`/${locale}/shops/${shop.slug}`);
  const address =
    shop.location?.formattedAddress ??
    [shop.location?.addressLine, area(shop, locale)].filter(Boolean).join(separator(locale));
  const gallery = shop.galleryUrls;
  const pros = professionals ?? [];

  return (
    <PublicShell variant="marketing">
      <JsonLd
        data={[
          {
            '@context': 'https://schema.org',
            '@type': 'HairSalon',
            '@id': url,
            name,
            url,
            image: [shop.coverUrl, ...gallery].filter(Boolean).map((u) => absoluteUrl(u!)),
            logo: shop.logoUrl ? absoluteUrl(shop.logoUrl) : undefined,
            description: (locale === 'en' ? shop.descriptionEn : shop.descriptionAr) ?? undefined,
            address: shop.location
              ? {
                  '@type': 'PostalAddress',
                  streetAddress: shop.location.addressLine ?? undefined,
                  addressLocality: shop.location.city ?? undefined,
                  addressRegion: shop.location.district ?? undefined,
                  addressCountry: 'SA',
                }
              : undefined,
            geo: shop.location
              ? {
                  '@type': 'GeoCoordinates',
                  latitude: shop.location.latitude,
                  longitude: shop.location.longitude,
                }
              : undefined,
            openingHoursSpecification: openingHoursLd(shop.openingHours),
            priceRange: shop.minPrice != null ? `${shop.currency ?? 'SAR'} ${shop.minPrice}+` : undefined,
            aggregateRating: aggregateRatingLd(shop.rating.average, shop.rating.count),
          },
          breadcrumbLd([
            { name: t('breadcrumb.home'), url: absoluteUrl(`/${locale}`) },
            { name: t('breadcrumb.shops'), url: absoluteUrl(`/${locale}/shops`) },
            { name, url },
          ]),
        ]}
      />
      <article className="mx-auto flex max-w-[960px] flex-col" data-testid="shop-page">
        <div className="relative h-[200px] overflow-hidden bg-bg-muted md:mt-4 md:h-[280px] md:rounded-section">
          {shop.coverUrl ? (
            <Image
              src={shop.coverUrl}
              alt=""
              fill
              loading="eager"
              fetchPriority="high"
              sizes="(min-width: 960px) 960px, 100vw"
              className="object-cover"
            />
          ) : (
            <ImagePlaceholder className="h-full w-full" />
          )}
        </div>

        <div className="flex flex-col gap-5 px-4 pb-6 md:px-6">
          <div className="-mt-8 flex items-end gap-4">
            <div className="relative flex size-[72px] shrink-0 items-center justify-center overflow-hidden rounded-card border-4 border-surface bg-chrome text-[1.75rem] font-extrabold text-on-chrome shadow-e2">
              {shop.logoUrl ? (
                <Image
                  src={shop.logoUrl}
                  alt={t('logoAlt', { name })}
                  fill
                  sizes="72px"
                  className="object-cover"
                />
              ) : (
                name.charAt(0)
              )}
            </div>
          </div>
          <Breadcrumb
            items={[
              { label: t('breadcrumb.home'), href: '/' },
              { label: t('breadcrumb.shops'), href: '/shops' },
              { label: name },
            ]}
          />
          <header className="flex flex-col gap-2">
            <div className="flex items-start justify-between gap-3">
              <h1 className="flex flex-wrap items-center gap-2 text-h1 font-bold text-navy-900">
                {name}
                {shop.isVerified && (
                  <span
                    className="inline-flex items-center gap-1 rounded-pill bg-brand-100 px-2.5 py-1 text-helper font-bold text-brand-700"
                    title={t('verifiedHint')}
                  >
                    <Icon name="shield" className="size-4" />
                    {t('verified')}
                    <span className="sr-only">{t('verifiedHint')}</span>
                  </span>
                )}
              </h1>
              {shop.listedInDiscovery && (
                <FavoriteButton target={{ kind: 'shop', id: shop.id }} name={name} mayBeSignedIn={signedIn} />
              )}
            </div>
            <p className="flex flex-wrap items-center gap-2 text-helper text-text-secondary">
              {shop.rating.count > 0 ? (
                <RatingStars value={shop.rating.average} count={shop.rating.count} />
              ) : (
                <span>{t('noReviews')}</span>
              )}
              {shop.location && (
                <ShopDistance latitude={shop.location.latitude} longitude={shop.location.longitude} />
              )}
            </p>
          </header>

          <Suspense fallback={<Skeleton className="h-7 w-48" />}>
            <LiveStatus slug={shop.slug} locale={locale} timeZone={shop.timeZone} />
          </Suspense>

          {shop.location && (
            <div className="flex items-center justify-between gap-3 rounded-card border border-border bg-surface p-3.5">
              <p className="flex min-w-0 items-center gap-2 text-helper text-text-strong">
                <Icon name="pin" className="size-5 shrink-0 text-brand-600" />
                <span className="sr-only">{t('address')}</span>
                <span className="truncate">{address}</span>
              </p>
              <a
                href={directionsUrl(shop.location.latitude, shop.location.longitude)}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex min-h-11 shrink-0 items-center text-label font-bold text-text-link hover:underline"
              >
                {t('directions')}
              </a>
            </div>
          )}

          {gallery.length > 0 && (
            <section aria-label={t('gallery')} className="-mx-4 overflow-x-auto px-4 md:mx-0 md:px-0">
              <ul className="flex w-max gap-3">
                {gallery.map((src, index) => (
                  <li
                    key={src}
                    className="relative h-[120px] w-[180px] overflow-hidden rounded-card bg-bg-muted"
                  >
                    <Image
                      src={src}
                      alt={t('galleryImage', { index: index + 1, total: gallery.length })}
                      fill
                      sizes="180px"
                      className="object-cover"
                    />
                  </li>
                ))}
              </ul>
            </section>
          )}

          <ShopTabs
            label={t('tabs.label')}
            initial={TABS.includes(tab as (typeof TABS)[number]) ? tab : 'services'}
            tabs={[
              {
                value: 'services',
                label: t('tabs.services'),
                content: (
                  <ServicesPanel
                    slug={shop.slug}
                    services={services ?? []}
                    packages={packages ?? []}
                    bookable={shop.listedInDiscovery}
                  />
                ),
              },
              {
                value: 'professionals',
                label: t('tabs.professionals'),
                content: (
                  <ProfessionalsPanel
                    slug={shop.slug}
                    professionals={pros}
                    nextTimes={Object.fromEntries(
                      pros.map((p) => [
                        p.id,
                        <Suspense key={p.id} fallback={<Skeleton className="h-6 w-20" />}>
                          <NextTime slug={shop.slug} professionalId={p.id} locale={locale} />
                        </Suspense>,
                      ]),
                    )}
                  />
                ),
              },
              {
                value: 'reviews',
                label: t('tabs.reviews'),
                content: (
                  <ReviewsPanel
                    reviews={reviews}
                    hrefForPage={(p) => `/shops/${shop.slug}?tab=reviews&reviewsPage=${p}`}
                  />
                ),
              },
              { value: 'about', label: t('tabs.about'), content: <AboutPanel shop={shop} name={name} /> },
            ]}
          />
        </div>
        <Suspense fallback={null}>
          <BookBar slug={shop.slug} locale={locale} />
        </Suspense>
      </article>
    </PublicShell>
  );
}
