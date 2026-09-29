import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { DiscoveryShopCard } from '@/components/discovery/ShopCards';
import { JsonLd } from '@/components/seo/JsonLd';
import { PublicShell } from '@/components/shell/PublicShell';
import { buttonClasses } from '@/components/ui/Button';
import { type DesignIconName, Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { readLocation } from '@/lib/discovery/location.server';
import { formatNumber, formatRating } from '@/lib/i18n/format';
import { organizationLd, websiteLd } from '@/lib/seo/jsonld';
import { absoluteUrl, localizedAlternates, OG_LOCALE } from '@/lib/seo/site';

export async function generateMetadata({ params }: PageProps<'/[locale]'>): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'landing.meta' });
  const app = await getTranslations({ locale, namespace: 'metadata' });
  return {
    title: { absolute: `${app('appName')} — ${t('title')}` },
    description: t('description'),
    alternates: localizedAlternates('/', locale),
    openGraph: {
      type: 'website',
      siteName: app('appName'),
      title: t('title'),
      description: t('description'),
      locale: OG_LOCALE[locale],
      url: `/${locale}`,
      images: [{ url: '/brand/trimme-logo.png', width: 371, height: 177, alt: app('appName') }],
    },
  };
}

const VALUES: Array<{ key: 'time' | 'choose' | 'whatsapp' | 'qr'; icon: DesignIconName }> = [
  { key: 'time', icon: 'clock' },
  { key: 'choose', icon: 'user' },
  { key: 'whatsapp', icon: 'msg' },
  { key: 'qr', icon: 'qr' },
];

/**
 * The marketing landing page (c-landing 819–913), indexable: hero with search, real platform figures (a figure that is
 * zero is not shown, spec §6), value propositions, the top-rated shops from stored reviews, and the partner band.
 */
export default async function LandingPage({ params }: PageProps<'/[locale]'>) {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'landing' });
  const app = await getTranslations({ locale, namespace: 'metadata' });
  const api = await getPublicApi();
  const [stats, top, areas, location] = await Promise.all([
    optional(() => api.GET('/api/v1/public/stats'), 'stats'),
    optional(
      () => api.GET('/api/v1/public/shops/search', { params: { query: { sort: 'Rating', pageSize: 4 } } }),
      'top rated',
    ),
    optional(() => api.GET('/api/v1/public/areas'), 'areas'),
    readLocation(),
  ]);
  const cities = [...new Set((areas?.areas ?? []).map((a) => a.city))];
  const city = cities.length === 1 ? cities[0] : null;
  const figures = [
    stats && stats.shopCount > 0
      ? { value: formatNumber(stats.shopCount, locale), label: t('stats.shops') }
      : null,
    stats && stats.professionalCount > 0
      ? { value: formatNumber(stats.professionalCount, locale), label: t('stats.professionals') }
      : null,
    stats && stats.reviewCount > 0
      ? { value: formatRating(stats.averageRating, locale), label: t('stats.rating') }
      : null,
  ].filter((figure): figure is { value: string; label: string } => figure !== null);
  const partnerContact = process.env.TRIMME_PARTNER_CONTACT_URL;

  return (
    <PublicShell variant="marketing">
      <JsonLd
        data={[
          organizationLd(absoluteUrl('/'), absoluteUrl('/brand/trimme-logo.png'), app('appName')),
          websiteLd(
            absoluteUrl(`/${locale}`),
            app('appName'),
            `${absoluteUrl(`/${locale}/search`)}?q={search_term_string}`,
            locale,
          ),
        ]}
      />
      <section className="mx-auto grid max-w-[1180px] items-center gap-10 px-4 py-10 md:px-6 md:py-14 lg:grid-cols-[1.05fr_1fr] lg:py-16">
        <div className="flex flex-col gap-5">
          <span className="inline-flex w-fit items-center gap-1.5 rounded-pill bg-brand-100 px-3 py-1.5 text-helper font-bold text-brand-700">
            <Icon name="pin" className="size-4" />
            {city ? t('badge', { city }) : t('badgeGeneric')}
          </span>
          <h1 className="text-[2rem] leading-[1.25] font-extrabold text-navy-900 md:text-[2.75rem]">
            {t('title')}
          </h1>
          <p className="max-w-[52ch] text-body text-text-secondary">{t('lead')}</p>
          <form
            action={`/${locale}/search`}
            method="get"
            role="search"
            aria-label={t('search.label')}
            className="flex flex-col gap-2 rounded-card border border-border bg-surface p-2 shadow-e2 sm:flex-row sm:items-center"
          >
            <label className="flex min-h-11 flex-1 items-center gap-2 px-2">
              <Icon name="search" className="size-5 shrink-0 text-text-tertiary" />
              <span className="sr-only">{t('search.label')}</span>
              <input
                name="q"
                type="search"
                placeholder={t('search.placeholder')}
                className="min-h-11 w-full bg-transparent text-input text-text-primary outline-none placeholder:text-text-placeholder"
              />
            </label>
            <Link
              href={{ pathname: '/onboarding/location', query: { returnTo: '/search' } }}
              className="inline-flex min-h-11 items-center gap-1.5 rounded-field px-3 text-label text-text-strong hover:bg-bg-subtle sm:border-s sm:border-border"
            >
              <Icon name="pin" className="size-4 text-brand-600" />
              <span className="sr-only">{t('search.location')}</span>
              {location ? location.label || t('search.location') : t('search.locationUnset')}
            </Link>
            <button
              type="submit"
              className="inline-flex min-h-11 items-center justify-center rounded-button bg-navy-900 px-6 text-button font-bold text-on-navy hover:bg-navy-800"
            >
              {t('search.submit')}
            </button>
          </form>
          {figures.length > 0 && (
            <dl aria-label={t('stats.label')} className="flex flex-wrap gap-8 pt-2">
              {figures.map((figure) => (
                <div key={figure.label} className="flex flex-col">
                  <dt className="order-2 text-helper text-text-secondary">{figure.label}</dt>
                  <dd className="order-1 font-latin text-[1.625rem] font-extrabold text-navy-900">
                    {figure.value}
                  </dd>
                </div>
              ))}
            </dl>
          )}
        </div>
        <div
          className="relative hidden aspect-[4/5] max-h-[520px] overflow-hidden rounded-section bg-navy-900 md:block"
          role="img"
          aria-label={t('heroAlt')}
        >
          <div
            aria-hidden="true"
            className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,var(--color-brand-500),transparent_55%),radial-gradient(circle_at_80%_90%,var(--color-navy-800),transparent_60%)] opacity-80"
          />
          <div aria-hidden="true" className="absolute inset-0 flex items-center justify-center">
            <Icon name="scissors" className="size-40 text-on-navy-subtle" />
          </div>
          <div
            aria-hidden="true"
            className="absolute start-6 bottom-8 flex flex-col gap-1 rounded-card bg-surface px-4 py-3 shadow-e3"
          >
            <span className="flex items-center gap-1.5 text-helper font-bold text-success-700">
              <span className="size-2 rounded-full bg-success-500" />
              {t('heroCard.title')}
            </span>
            <span className="text-helper text-text-secondary">{t('heroCard.body')}</span>
          </div>
        </div>
      </section>

      <section aria-labelledby="values-heading" className="mx-auto max-w-[1180px] px-4 pb-12 md:px-6">
        <h2 id="values-heading" className="sr-only">
          {t('values.label')}
        </h2>
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {VALUES.map(({ key, icon }) => (
            <li
              key={key}
              className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
            >
              <span className="flex size-10 items-center justify-center rounded-field bg-brand-100 text-brand-700">
                <Icon name={icon} className="size-5" />
              </span>
              <h3 className="text-[1rem] font-bold text-text-primary">{t(`values.${key}.title`)}</h3>
              <p className="text-helper text-text-secondary">{t(`values.${key}.body`)}</p>
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="top-heading" className="mx-auto max-w-[1180px] px-4 pb-12 md:px-6">
        <div className="mb-4 flex items-end justify-between gap-4">
          <h2 id="top-heading" className="text-h2 font-bold text-navy-900">
            {city ? t('topRated.titleCity', { city }) : t('topRated.title')}
          </h2>
          <Link
            href="/shops"
            className="inline-flex min-h-11 items-center text-label font-bold text-text-link hover:underline"
          >
            {t('topRated.viewAll')}
          </Link>
        </div>
        {top && top.items.length > 0 ? (
          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {top.items.map((shop) => (
              <li key={shop.id}>
                <DiscoveryShopCard shop={shop} />
              </li>
            ))}
          </ul>
        ) : (
          <p className="rounded-card border border-dashed border-border-dashed p-6 text-center text-body text-text-secondary">
            {t('topRated.empty')}
          </p>
        )}
      </section>

      <section className="mx-auto max-w-[1180px] px-4 pb-16 md:px-6">
        <div className="flex flex-col gap-5 rounded-section bg-navy-900 p-8 text-on-navy md:flex-row md:items-center md:justify-between md:p-10">
          <div className="flex flex-col gap-2">
            <h2 className="text-h2 font-bold">{t('partner.title')}</h2>
            <p className="max-w-[56ch] text-body text-on-navy-muted">{t('partner.body')}</p>
          </div>
          {partnerContact && (
            // An external address (mailto: or a form), so a plain anchor: no locale prefix is added.
            <a
              href={partnerContact}
              className={`${buttonClasses({ variant: 'secondary', size: 'lg' })} shrink-0`}
            >
              {t('partner.cta')}
            </a>
          )}
        </div>
      </section>
    </PublicShell>
  );
}
