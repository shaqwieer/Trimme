import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { LegalPage } from '@/components/legal/LegalPage';
import { asLocale } from '@/i18n/routing';
import { localizedAlternates } from '@/lib/seo/site';

/** Rendered per request so canonical URLs use the deployed origin (TRIMME_SITE_URL is runtime configuration). */
export const dynamic = 'force-dynamic';

export async function generateMetadata({ params }: PageProps<'/[locale]/privacy'>): Promise<Metadata> {
  const locale = asLocale((await params).locale);
  const t = await getTranslations({ locale, namespace: 'legal.privacy' });
  return {
    title: t('title'),
    description: t('description'),
    alternates: localizedAlternates('/privacy', locale),
  };
}

export default async function PrivacyPage({ params }: PageProps<'/[locale]/privacy'>) {
  return <LegalPage kind="privacy" locale={asLocale((await params).locale)} />;
}
