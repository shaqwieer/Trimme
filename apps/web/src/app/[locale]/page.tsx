import { useTranslations } from 'next-intl';
import { Logo } from '@/components/brand/Logo';
import { PublicShell } from '@/components/shell/PublicShell';

/** Placeholder home until the landing page is built in Phase 11. */
export default function HomePage() {
  const t = useTranslations('home');

  return (
    <PublicShell>
      <section className="mx-auto flex max-w-[1180px] flex-col items-center gap-6 px-4 py-16 text-center md:px-6 md:py-24">
        <Logo height={96} priority />
        <h1 className="text-display font-bold text-navy-900">{t('title')}</h1>
        <p className="max-w-[46ch] text-body text-text-secondary">{t('subtitle')}</p>
        <p className="rounded-button border border-border bg-surface px-4 py-3 text-caption text-text-strong shadow-e1">
          {t('comingSoon')}
        </p>
      </section>
    </PublicShell>
  );
}
