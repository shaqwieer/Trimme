import { getTranslations } from 'next-intl/server';
import { SessionsList } from '@/components/auth/SessionsList';
import { requireUser } from '@/lib/auth/server';

/** Security and sessions (spec §9 revoke-all, R-AUTH-05). Available to every signed-in user type. */
export default async function AccountSecurityPage({ params }: PageProps<'/[locale]/account/security'>) {
  const { locale } = await params;
  await requireUser(locale, '/account/security');
  const t = await getTranslations({ locale: locale === 'en' ? 'en' : 'ar', namespace: 'account' });

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6">
      <header className="flex flex-col gap-1">
        <h1 className="text-page-title font-bold text-navy-900">{t('security')}</h1>
        <p className="text-caption text-text-secondary">{t('securityDescription')}</p>
      </header>
      <SessionsList />
    </div>
  );
}
