import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LocationChooser } from '@/components/discovery/LocationChooser';
import { PublicShell } from '@/components/shell/PublicShell';
import { Icon } from '@/components/ui/icons';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { NO_INDEX } from '@/lib/seo/site';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/onboarding/location'>): Promise<Metadata> {
  const t = await getTranslations({ locale: asLocale((await params).locale), namespace: 'location.meta' });
  return { title: t('title'), robots: NO_INDEX };
}

/** Only same-site relative paths are followed after choosing (no open redirects). */
function safeReturnTo(value: string | string[] | undefined): string {
  const path = Array.isArray(value) ? value[0] : value;
  return path && path.startsWith('/') && !path.startsWith('//') && !path.startsWith('/\\')
    ? path
    : '/discover';
}

/**
 * Location permission (c-auth "LOCATION PERMISSION" 972–987) with the manual district choice the design only hints at
 * (DV-A21). Personal, so never indexed.
 */
export default async function LocationOnboardingPage({
  params,
  searchParams,
}: PageProps<'/[locale]/onboarding/location'>) {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'location' });
  const returnTo = safeReturnTo((await searchParams).returnTo);
  const api = await getPublicApi();
  const areas = await optional(() => api.GET('/api/v1/public/areas'), 'areas');

  return (
    <PublicShell>
      <div className="mx-auto flex w-full max-w-[480px] flex-col">
        <div
          role="img"
          aria-label={t('mapPreview')}
          className="relative h-[220px] bg-bg-subtle bg-[repeating-linear-gradient(135deg,var(--color-brand-100)_0_12px,var(--color-bg-subtle)_12px_24px)]"
        >
          <span className="absolute inset-0 flex items-center justify-center">
            <span className="flex size-14 items-center justify-center rounded-full bg-surface shadow-e2">
              <Icon name="pin" className="size-7 text-navy-900" />
            </span>
          </span>
        </div>
        <section className="-mt-6 flex flex-col gap-4 rounded-t-section bg-surface px-5 pt-6 pb-10 shadow-sheet">
          <span className="flex size-12 items-center justify-center rounded-card bg-brand-100 text-brand-700">
            <Icon name="pin" className="size-6" />
          </span>
          <h1 className="text-h2 font-bold text-navy-900">{t('title')}</h1>
          <p className="text-body text-text-secondary">{t('body')}</p>
          <LocationChooser areas={areas?.areas ?? []} returnTo={returnTo} />
          <Link
            href={returnTo}
            className="inline-flex min-h-11 items-center justify-center text-label font-bold text-text-link hover:underline"
          >
            {t('skip')}
          </Link>
        </section>
      </div>
    </PublicShell>
  );
}
