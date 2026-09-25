import { useTranslations } from 'next-intl';
import { PublicShell } from '@/components/shell/PublicShell';
import { Link } from '@/i18n/navigation';

export default function NotFound() {
  const t = useTranslations('errors.notFound');

  return (
    <PublicShell>
      <section className="mx-auto flex max-w-[36rem] flex-col items-center gap-4 px-4 py-24 text-center">
        <h1 className="text-h1 font-bold text-navy-900">{t('title')}</h1>
        <p className="text-body text-text-secondary">{t('body')}</p>
        <Link
          href="/"
          className="inline-flex min-h-11 items-center rounded-button bg-navy-900 px-5 text-button font-bold text-on-navy hover:bg-navy-800"
        >
          {t('backHome')}
        </Link>
      </section>
    </PublicShell>
  );
}
