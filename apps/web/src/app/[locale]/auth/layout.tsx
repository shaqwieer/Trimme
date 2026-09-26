import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { PublicShell } from '@/components/shell/PublicShell';

/** Auth screens are private flows: never indexed (spec §6). */
export async function generateMetadata({ params }: LayoutProps<'/[locale]/auth'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: locale === 'en' ? 'en' : 'ar', namespace: 'auth.meta' });
  return { title: t('title'), robots: { index: false, follow: false } };
}

export default function AuthLayout({ children }: LayoutProps<'/[locale]/auth'>) {
  return <PublicShell>{children}</PublicShell>;
}
