import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { JsonLd } from '@/components/seo/JsonLd';
import { PublicShell } from '@/components/shell/PublicShell';
import { ReviewsPanel } from '@/components/shops/public/ShopPanels';
import { Avatar } from '@/components/ui/Avatar';
import { ButtonLink } from '@/components/ui/Button';
import { Breadcrumb } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { RatingStars } from '@/components/ui/Rating';
import { EmptyState, Skeleton } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { bookHref, getPublicProfessional, getPublicShop } from '@/lib/discovery/shop-data';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatPrice,
  formatTime,
} from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { breadcrumbLd } from '@/lib/seo/jsonld';
import { absoluteUrl, localizedAlternates, NO_INDEX, OG_LOCALE } from '@/lib/seo/site';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/shops/[slug]/professionals/[proSlug]'>): Promise<Metadata> {
  const { locale: raw, slug, proSlug } = await params;
  const locale = asLocale(raw);
  const [pro, shop] = await Promise.all([getPublicProfessional(slug, proSlug), getPublicShop(slug)]);
  if (!pro || !shop) return {};
  const t = await getTranslations({ locale, namespace: 'proPage.meta' });
  const name = localizedName(locale, pro.nameAr, pro.nameEn);
  const shopName = localizedName(locale, pro.shopNameAr, pro.shopNameEn);
  const specialty = localizedName(locale, pro.specialtyAr ?? '', pro.specialtyEn) || shopName;
  const title = t('title', { name, shop: shopName });
  const description = t('description', { name, specialty, shop: shopName });
  return {
    title,
    description,
    alternates: localizedAlternates(`/shops/${pro.shopSlug}/professionals/${pro.slug}`, locale),
    robots: shop.listedInDiscovery ? undefined : NO_INDEX,
    openGraph: {
      type: 'profile',
      title,
      description,
      locale: OG_LOCALE[locale],
      url: `/${locale}/shops/${pro.shopSlug}/professionals/${pro.slug}`,
      images: [{ url: pro.avatarUrl ?? shop.coverUrl ?? '/brand/trimme-logo.png' }],
    },
  };
}

/** "Earliest available times today" (c-shop 1386–1392): live, never cached; each chip opens the wizard at that time. */
async function NextSlots({
  slug,
  proSlug,
  proId,
  locale,
  timeZone,
}: {
  slug: string;
  proSlug: string;
  proId: string;
  locale: AppLocale;
  timeZone: string;
}) {
  const t = await getTranslations({ locale, namespace: 'proPage.next' });
  const api = await getPublicApi();
  const next = await optional(
    () =>
      api.GET('/api/v1/public/shops/{slug}/professionals/{professionalSlug}/next-slots', {
        params: { path: { slug, professionalSlug: proSlug } },
      }),
    'next slots',
  );
  if (!next) return null;
  if (!next.bookable) return <p className="text-helper text-text-secondary">{t('unavailable')}</p>;
  if (!next.date || next.slots.length === 0 || !next.offer)
    return <p className="text-helper text-text-secondary">{t('none')}</p>;

  const today = new Intl.DateTimeFormat('en-CA', { timeZone }).format(new Date());
  const title =
    next.date === today
      ? t('titleToday')
      : t('titleOn', { day: formatDate(`${next.date}T12:00:00Z`, locale, { timeZone: 'UTC' }) });
  const offer = next.offer;
  return (
    <section aria-labelledby="next-heading" className="flex flex-col gap-3">
      <h2 id="next-heading" className="text-h3 font-bold text-navy-900">
        {title}
      </h2>
      <ul className="flex flex-wrap gap-2">
        {next.slots.map((slot) => {
          const time = formatTime(slot.startsAt, locale, timeZone);
          return (
            <li key={slot.startsAt}>
              <Link
                href={bookHref(slug, {
                  pro: proId,
                  [offer.isPackage ? 'package' : 'service']: offer.id,
                  date: next.date ?? undefined,
                  time: slot.localTime,
                })}
                aria-label={t('slot', { time })}
                className="inline-flex min-h-11 items-center rounded-field border border-border bg-surface px-4 text-label font-bold text-navy-900 hover:border-brand-500"
              >
                {time}
              </Link>
            </li>
          );
        })}
        {next.remainingCount > 0 && (
          <li className="inline-flex min-h-11 items-center rounded-field border border-dashed border-border-dashed px-4 text-label text-text-secondary">
            {t('more', { count: next.remainingCount })}
          </li>
        )}
      </ul>
    </section>
  );
}

/**
 * A professional's public profile within their shop (c-shop "PROFESSIONAL PROFILE" 1365–1418). No phone or WhatsApp
 * number anywhere (spec §8). The design's "completed appointments" and "punctuality" figures are not shown: there is no
 * agreed definition yet, and invented numbers are not allowed (spec §6).
 */
export default async function ProfessionalPage({
  params,
}: PageProps<'/[locale]/shops/[slug]/professionals/[proSlug]'>) {
  const { locale: raw, slug, proSlug } = await params;
  const locale = asLocale(raw);
  const [pro, shop] = await Promise.all([getPublicProfessional(slug, proSlug), getPublicShop(slug)]);
  if (!pro || !shop) notFound();

  const t = await getTranslations({ locale, namespace: 'proPage' });
  const tCrumb = await getTranslations({ locale, namespace: 'shopPage.breadcrumb' });
  const tServices = await getTranslations({ locale, namespace: 'shopPage.services' });
  const api = await getPublicApi();
  const reviews = await optional(
    () =>
      api.GET('/api/v1/public/shops/{slug}/reviews', {
        params: { path: { slug: pro.shopSlug }, query: { professionalId: pro.id, pageSize: 3 } },
      }),
    'reviews',
  );
  const name = localizedName(locale, pro.nameAr, pro.nameEn);
  const shopName = localizedName(locale, pro.shopNameAr, pro.shopNameEn);
  const specialty =
    pro.specialtyAr || pro.specialtyEn ? localizedName(locale, pro.specialtyAr ?? '', pro.specialtyEn) : null;
  const bio = locale === 'en' ? (pro.bioEn ?? pro.bioAr) : (pro.bioAr ?? pro.bioEn);
  const shopUrl = absoluteUrl(`/${locale}/shops/${pro.shopSlug}`);
  const url = `${shopUrl}/professionals/${pro.slug}`;
  const district = shop.location?.district;

  return (
    <PublicShell variant="marketing">
      <JsonLd
        data={[
          {
            '@context': 'https://schema.org',
            '@type': 'Person',
            name,
            jobTitle: specialty ?? undefined,
            image: pro.avatarUrl ? absoluteUrl(pro.avatarUrl) : undefined,
            url,
            worksFor: { '@type': 'HairSalon', name: shopName, url: shopUrl },
          },
          breadcrumbLd([
            { name: tCrumb('home'), url: absoluteUrl(`/${locale}`) },
            { name: tCrumb('shops'), url: absoluteUrl(`/${locale}/shops`) },
            { name: shopName, url: shopUrl },
            { name, url },
          ]),
        ]}
      />
      <article
        className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6"
        data-testid="professional-page"
      >
        <Breadcrumb
          items={[
            { label: tCrumb('home'), href: '/' },
            { label: tCrumb('shops'), href: '/shops' },
            { label: shopName, href: `/shops/${pro.shopSlug}` },
            { label: name },
          ]}
        />
        <section className="flex flex-col items-center gap-3 rounded-card border border-border bg-surface p-6 text-center shadow-e1">
          <Avatar name={name} src={pro.avatarUrl} size="xl" />
          <h1 className="text-h1 font-bold text-navy-900">{name}</h1>
          {specialty && <p className="text-body text-text-secondary">{specialty}</p>}
          {pro.reviewCount > 0 && <RatingStars value={pro.rating} count={pro.reviewCount} size="md" />}
          {bio && <p className="max-w-[52ch] text-body text-text-strong">{bio}</p>}
          <Link
            href={`/shops/${pro.shopSlug}`}
            className="inline-flex min-h-11 items-center gap-1.5 rounded-field bg-brand-100 px-4 text-label font-bold text-brand-700 hover:bg-brand-150"
          >
            <Icon name="store" className="size-4" />
            {t('worksAt', { shop: district ? `${shopName} — ${district}` : shopName })}
          </Link>
        </section>

        <Suspense fallback={<Skeleton className="h-20 w-full" />}>
          <NextSlots
            slug={pro.shopSlug}
            proSlug={pro.slug}
            proId={pro.id}
            locale={locale}
            timeZone={shop.timeZone}
          />
        </Suspense>

        <section aria-labelledby="pro-services" className="flex flex-col gap-3">
          <h2 id="pro-services" className="text-h3 font-bold text-navy-900">
            {t('services.title')}
          </h2>
          {pro.offers.length === 0 ? (
            <EmptyState icon="scissors" title={t('services.empty')} />
          ) : (
            <ul className="flex flex-col gap-2">
              {pro.offers.map((offer) => {
                const offerName = localizedName(locale, offer.nameAr, offer.nameEn);
                return (
                  <li
                    key={offer.id}
                    className="flex items-center justify-between gap-3 rounded-card border border-border bg-surface p-4"
                  >
                    <div className="flex flex-col">
                      <span className="text-label font-bold text-text-primary">{offerName}</span>
                      <span className="text-helper text-text-tertiary">
                        {formatDurationMinutes(offer.durationMinutes, locale)}
                      </span>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className="font-latin text-label font-extrabold text-navy-900">
                        {formatPrice(offer.price, locale, offer.currency)}
                      </span>
                      {shop.listedInDiscovery && (
                        <ButtonLink
                          href={bookHref(pro.shopSlug, {
                            pro: pro.id,
                            [offer.isPackage ? 'package' : 'service']: offer.id,
                          })}
                          variant="secondary"
                          size="xs"
                          aria-label={tServices('bookLabel', { name: offerName })}
                        >
                          {tServices('book')}
                        </ButtonLink>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        <section aria-labelledby="pro-reviews" className="flex flex-col gap-3">
          <div className="flex items-center justify-between gap-3">
            <h2 id="pro-reviews" className="text-h3 font-bold text-navy-900">
              {t('reviews.title')}
            </h2>
            {reviews && reviews.summary.count > 3 && (
              <Link
                href={`/shops/${pro.shopSlug}?tab=reviews`}
                className="inline-flex min-h-11 items-center text-label font-bold text-text-link hover:underline"
              >
                {t('reviews.all')}
              </Link>
            )}
          </div>
          <ReviewsPanel
            reviews={reviews}
            showNote={false}
            hrefForPage={() => `/shops/${pro.shopSlug}?tab=reviews`}
          />
        </section>
      </article>
      {shop.listedInDiscovery && (
        <div className="sticky bottom-0 z-10 border-t border-border bg-surface/95 backdrop-blur-md">
          <div className="mx-auto max-w-[720px] px-4 py-3 md:px-6">
            <ButtonLink href={bookHref(pro.shopSlug, { pro: pro.id })} variant="primary" size="lg" fullWidth>
              {t('book', { name: name.split(' ')[0] ?? name })}
            </ButtonLink>
          </div>
        </div>
      )}
    </PublicShell>
  );
}
