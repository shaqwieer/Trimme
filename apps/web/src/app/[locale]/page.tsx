import Image from 'next/image';
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
      images: [
        { url: '/brand/trimme-hero-barbershop.png', width: 1152, height: 1536, alt: t('heroImageAlt') },
      ],
    },
  };
}

const TRUST_ITEMS: Array<{ key: 'time' | 'choose' | 'whatsapp'; icon: DesignIconName }> = [
  { key: 'time', icon: 'clock' },
  { key: 'choose', icon: 'user' },
  { key: 'whatsapp', icon: 'msg' },
];

const STEPS: Array<{ key: 'find' | 'choose' | 'confirm'; icon: DesignIconName }> = [
  { key: 'find', icon: 'search' },
  { key: 'choose', icon: 'calendar' },
  { key: 'confirm', icon: 'check' },
];

const OUTCOMES: Array<{ key: 'noCalls' | 'clearChoice' | 'rightFit'; icon: DesignIconName }> = [
  { key: 'noCalls', icon: 'phone' },
  { key: 'clearChoice', icon: 'star' },
  { key: 'rightFit', icon: 'clock' },
];

const FAQ_KEYS = ['cost', 'download', 'barber', 'confirmation', 'changes'] as const;

/** Arabic-first acquisition page: customer discovery is primary; shop partnership is secondary. */
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
  const cities = [...new Set((areas?.areas ?? []).map((area) => area.city))];
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
    <PublicShell variant="landing">
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

      <section className="relative overflow-hidden border-b border-border-subtle bg-surface">
        <div
          aria-hidden="true"
          className="absolute inset-x-0 top-0 h-[420px] bg-[radial-gradient(circle_at_15%_15%,var(--color-brand-150),transparent_58%)] opacity-70 rtl:bg-[radial-gradient(circle_at_85%_15%,var(--color-brand-150),transparent_58%)]"
        />
        <div className="relative mx-auto grid max-w-[1280px] items-center gap-9 px-4 py-8 md:px-6 md:py-12 lg:grid-cols-[1.04fr_0.96fr] lg:gap-14 lg:py-16 xl:px-10">
          <div className="flex flex-col items-start gap-5">
            <span className="inline-flex items-center gap-1.5 rounded-pill bg-brand-100 px-3 py-1.5 text-helper font-bold text-brand-700">
              <Icon name="pin" className="size-4" />
              {city ? t('badge', { city }) : t('badgeGeneric')}
            </span>
            <div className="flex max-w-[650px] flex-col gap-4">
              <h1 className="text-[2.25rem] leading-[1.18] font-extrabold tracking-[-0.025em] text-navy-900 md:text-[3.25rem] lg:text-[3.55rem] rtl:tracking-normal">
                {t('title')}
              </h1>
              <p className="max-w-[58ch] text-[1rem] leading-8 text-text-secondary md:text-[1.0625rem]">
                {t('lead')}
              </p>
            </div>

            <form
              action={`/${locale}/search`}
              method="get"
              role="search"
              aria-label={t('search.label')}
              className="grid w-full max-w-[680px] gap-2 rounded-section border border-border bg-surface p-2 shadow-e3 sm:grid-cols-[minmax(0,1fr)_auto_auto] sm:items-center"
            >
              <label className="flex min-h-12 items-center gap-2 px-2">
                <Icon name="search" className="size-5 text-text-tertiary" />
                <span className="sr-only">{t('search.label')}</span>
                <input
                  name="q"
                  type="search"
                  placeholder={t('search.placeholder')}
                  className="min-h-12 w-full bg-transparent text-input text-text-primary outline-none placeholder:text-text-placeholder"
                />
              </label>
              <Link
                href={{ pathname: '/onboarding/location', query: { returnTo: '/search' } }}
                className="inline-flex min-h-12 items-center gap-1.5 rounded-field border-t border-border px-3 text-label font-bold text-text-strong hover:bg-bg-subtle sm:border-s sm:border-t-0"
              >
                <Icon name="pin" className="size-4 text-brand-600" />
                {location ? location.label || t('search.location') : t('search.locationUnset')}
              </Link>
              <button
                type="submit"
                className="inline-flex min-h-12 items-center justify-center gap-2 rounded-button bg-navy-900 px-5 text-button font-bold text-on-navy shadow-e1 transition hover:bg-navy-800 hover:shadow-button-hover"
              >
                {t('search.submit')}
                <Icon name="chevR" className="size-4" />
              </button>
            </form>

            <Link
              href="/shops"
              className="inline-flex min-h-11 items-center gap-1.5 text-label font-bold text-text-link hover:underline"
            >
              {t('browse')}
              <Icon name="chevR" className="size-4" />
            </Link>

            {figures.length > 0 && (
              <dl
                aria-label={t('stats.label')}
                className="flex flex-wrap gap-x-8 gap-y-3 border-t border-border-subtle pt-5"
              >
                {figures.map((figure) => (
                  <div key={figure.label} className="flex flex-col">
                    <dt className="order-2 text-helper text-text-secondary">{figure.label}</dt>
                    <dd className="order-1 font-latin text-[1.5rem] font-extrabold text-navy-900">
                      {figure.value}
                    </dd>
                  </div>
                ))}
              </dl>
            )}
          </div>

          <div className="relative mx-auto aspect-[4/5] w-full max-w-[540px] overflow-hidden rounded-[24px] bg-navy-900 shadow-e3">
            <Image
              src="/brand/trimme-hero-barbershop.png"
              alt={t('heroImageAlt')}
              fill
              priority
              sizes="(min-width: 1200px) 500px, (min-width: 768px) 44vw, 92vw"
              className="object-cover object-center"
            />
            <div
              aria-hidden="true"
              className="absolute inset-x-0 bottom-0 h-2/5 bg-gradient-to-t from-navy-950/55 to-transparent"
            />
            <div className="border-white/60 bg-white/95 absolute start-4 bottom-4 max-w-[calc(100%-2rem)] rounded-card border px-4 py-3 shadow-e3 backdrop-blur md:start-6 md:bottom-6">
              <span className="flex items-center gap-2 text-helper font-bold text-success-700">
                <span className="flex size-5 items-center justify-center rounded-full bg-success-50">
                  <Icon name="check" className="size-3.5" />
                </span>
                {t('heroCard.title')}
              </span>
              <span className="mt-1 block text-helper text-text-secondary">{t('heroCard.body')}</span>
            </div>
          </div>
        </div>
      </section>

      <section aria-labelledby="trust-heading" className="bg-surface">
        <div className="mx-auto max-w-[1280px] px-4 py-7 md:px-6 xl:px-10">
          <h2 id="trust-heading" className="sr-only">
            {t('trust.label')}
          </h2>
          <ul className="grid gap-3 md:grid-cols-3 md:divide-x md:divide-border-subtle rtl:md:divide-x-reverse">
            {TRUST_ITEMS.map(({ key, icon }) => (
              <li
                key={key}
                className="flex items-center gap-3 rounded-card bg-bg-tile p-4 md:rounded-none md:bg-transparent md:px-6 first:md:ps-0 last:md:pe-0"
              >
                <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
                  <Icon name={icon} className="size-5" />
                </span>
                <div>
                  <h3 className="text-[0.9375rem] font-bold text-text-primary">{t(`trust.${key}.title`)}</h3>
                  <p className="mt-0.5 text-helper text-text-secondary">{t(`trust.${key}.body`)}</p>
                </div>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section
        aria-labelledby="top-heading"
        className="mx-auto max-w-[1280px] px-4 py-14 md:px-6 md:py-20 xl:px-10"
      >
        <div className="mb-7 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <span className="text-eyebrow font-bold tracking-[0.14em] text-brand-700 uppercase">
              {t('topRated.eyebrow')}
            </span>
            <h2
              id="top-heading"
              className="mt-2 text-[1.75rem] leading-tight font-extrabold text-navy-900 md:text-[2.25rem]"
            >
              {city ? t('topRated.titleCity', { city }) : t('topRated.title')}
            </h2>
            <p className="mt-2 max-w-[60ch] text-body text-text-secondary">{t('topRated.body')}</p>
          </div>
          <Link
            href="/shops"
            className="inline-flex min-h-11 items-center gap-1 text-label font-bold text-text-link hover:underline"
          >
            {t('topRated.viewAll')}
            <Icon name="chevR" className="size-4" />
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
          <div className="rounded-section border border-dashed border-border-dashed bg-surface p-8 text-center">
            <Icon name="store" className="mx-auto size-8 text-brand-600" />
            <p className="mt-3 text-body text-text-secondary">{t('topRated.empty')}</p>
            <Link
              href="/shops"
              className="mt-3 inline-flex min-h-11 items-center text-label font-bold text-text-link hover:underline"
            >
              {t('topRated.viewAll')}
            </Link>
          </div>
        )}
      </section>

      <section
        id="how-it-works"
        aria-labelledby="steps-heading"
        className="scroll-mt-20 border-y border-border-subtle bg-surface"
      >
        <div className="mx-auto max-w-[1120px] px-4 py-14 md:px-6 md:py-20">
          <div className="mx-auto max-w-[650px] text-center">
            <span className="text-eyebrow font-bold tracking-[0.14em] text-brand-700 uppercase">
              {t('steps.eyebrow')}
            </span>
            <h2
              id="steps-heading"
              className="mt-2 text-[1.75rem] font-extrabold text-navy-900 md:text-[2.25rem]"
            >
              {t('steps.title')}
            </h2>
            <p className="mt-3 text-body text-text-secondary">{t('steps.body')}</p>
          </div>
          <ol className="mt-10 grid gap-5 md:grid-cols-3">
            {STEPS.map(({ key, icon }, index) => (
              <li
                key={key}
                className="relative rounded-section border border-border bg-bg-tile p-6 shadow-e1"
              >
                <span className="absolute end-5 top-5 font-latin text-[2rem] font-extrabold text-brand-600">
                  {String(index + 1).padStart(2, '0')}
                </span>
                <span className="flex size-11 items-center justify-center rounded-button bg-navy-900 text-on-navy">
                  <Icon name={icon} className="size-5" />
                </span>
                <h3 className="mt-5 text-[1rem] font-bold text-text-primary">{t(`steps.${key}.title`)}</h3>
                <p className="mt-2 text-helper text-text-secondary">{t(`steps.${key}.body`)}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <section
        aria-labelledby="outcomes-heading"
        className="mx-auto grid max-w-[1180px] gap-10 px-4 py-14 md:px-6 md:py-20 lg:grid-cols-[0.9fr_1.1fr] lg:items-center"
      >
        <div>
          <span className="text-eyebrow font-bold tracking-[0.14em] text-brand-700 uppercase">
            {t('outcomes.eyebrow')}
          </span>
          <h2
            id="outcomes-heading"
            className="mt-2 text-[1.75rem] leading-tight font-extrabold text-navy-900 md:text-[2.25rem]"
          >
            {t('outcomes.title')}
          </h2>
          <p className="mt-3 max-w-[56ch] text-body text-text-secondary">{t('outcomes.body')}</p>
          <Link href="/discover" className={`${buttonClasses({ variant: 'primary', size: 'lg' })} mt-6`}>
            {t('outcomes.cta')}
          </Link>
        </div>
        <ul className="grid gap-4">
          {OUTCOMES.map(({ key, icon }) => (
            <li
              key={key}
              className="flex gap-4 rounded-section border border-border bg-surface p-5 shadow-e1"
            >
              <span className="flex size-11 shrink-0 items-center justify-center rounded-button bg-brand-100 text-brand-700">
                <Icon name={icon} className="size-5" />
              </span>
              <div>
                <h3 className="font-bold text-text-primary">{t(`outcomes.${key}.title`)}</h3>
                <p className="mt-1 text-helper text-text-secondary">{t(`outcomes.${key}.body`)}</p>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section id="for-shops" className="scroll-mt-20 px-4 pb-14 md:px-6 md:pb-20">
        <div className="relative mx-auto max-w-[1180px] overflow-hidden rounded-[24px] bg-navy-900 px-6 py-10 text-on-navy md:px-10 md:py-12">
          <div
            aria-hidden="true"
            className="absolute -end-16 -top-24 size-64 rounded-full bg-brand-500/20 blur-3xl"
          />
          <div className="relative grid gap-8 md:grid-cols-[1fr_auto] md:items-center">
            <div>
              <span className="text-eyebrow font-bold tracking-[0.14em] text-on-navy-accent uppercase">
                {t('partner.eyebrow')}
              </span>
              <h2 className="mt-2 max-w-[22ch] text-[1.75rem] leading-tight font-extrabold md:text-[2.25rem]">
                {t('partner.title')}
              </h2>
              <p className="mt-3 max-w-[65ch] text-body text-on-navy-muted">{t('partner.body')}</p>
            </div>
            {partnerContact && (
              <a
                href={partnerContact}
                className={`${buttonClasses({ variant: 'secondary', size: 'lg' })} shrink-0`}
              >
                {t('partner.cta')}
              </a>
            )}
          </div>
        </div>
      </section>

      <section aria-labelledby="faq-heading" className="border-y border-border-subtle bg-surface">
        <div className="mx-auto grid max-w-[1080px] gap-9 px-4 py-14 md:px-6 md:py-20 lg:grid-cols-[0.72fr_1.28fr]">
          <div>
            <span className="text-eyebrow font-bold tracking-[0.14em] text-brand-700 uppercase">
              {t('faq.eyebrow')}
            </span>
            <h2
              id="faq-heading"
              className="mt-2 text-[1.75rem] font-extrabold text-navy-900 md:text-[2.25rem]"
            >
              {t('faq.title')}
            </h2>
            <p className="mt-3 text-body text-text-secondary">{t('faq.body')}</p>
          </div>
          <div className="divide-y divide-border rounded-section border border-border px-5">
            {FAQ_KEYS.map((key, index) => (
              <details key={key} className="group py-1" open={index === 0}>
                <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 py-3 font-bold text-text-primary marker:hidden">
                  {t(`faq.${key}.question`)}
                  <Icon
                    name="plus"
                    className="size-5 text-brand-700 transition-transform group-open:rotate-45"
                  />
                </summary>
                <p className="max-w-[70ch] pb-5 text-helper text-text-secondary">{t(`faq.${key}.answer`)}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      <section className="px-4 py-14 md:px-6 md:py-20">
        <div className="mx-auto flex max-w-[900px] flex-col items-center rounded-[24px] bg-brand-100 px-6 py-10 text-center md:px-10 md:py-12">
          <Icon name="scissors" className="size-8 text-brand-700" />
          <h2 className="mt-4 text-[1.75rem] font-extrabold text-navy-900 md:text-[2.25rem]">
            {t('finalCta.title')}
          </h2>
          <p className="mt-3 max-w-[56ch] text-body text-text-secondary">{t('finalCta.body')}</p>
          <Link href="/shops" className={`${buttonClasses({ variant: 'primary', size: 'lg' })} mt-6`}>
            {t('finalCta.cta')}
          </Link>
        </div>
      </section>
    </PublicShell>
  );
}
