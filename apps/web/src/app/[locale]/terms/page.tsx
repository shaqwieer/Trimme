import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LegalPage } from '@/components/legal/LegalPage';
import { asLocale } from '@/i18n/routing';
import { localizedAlternates } from '@/lib/seo/site';

/** Rendered per request so canonical URLs use the deployed origin (TRIMME_SITE_URL is runtime configuration). */
export const dynamic = 'force-dynamic';

export async function generateMetadata({ params }: PageProps<'/[locale]/terms'>): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'legal.terms' });
  return {
    title: t('title'),
    description: t('description'),
    alternates: localizedAlternates('/terms', locale),
  };
}

export default async function TermsPage({ params }: PageProps<'/[locale]/terms'>) {
  return <LegalPage kind="terms" locale={asLocale((await params).locale)} />;
}
