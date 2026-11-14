import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ProfileForm } from '@/components/account/ProfileForm';
import { Breadcrumb } from '@/components/ui/data';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { requireCustomer } from '@/lib/auth/server';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/profile'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'account.profile' });
  return { title: t('title') };
}

/** Edit the customer's name, email, mobile number and language (c-profile, personal info, D-144). */
export default async function ProfilePage({ params }: PageProps<'/[locale]/account/profile'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const me = await requireCustomer(locale, '/account/profile');
  if (!me) return <PermissionDenied homeHref="/" />;
  const [t, tAccount, { data: profile, response }] = await Promise.all([
    getTranslations({ locale, namespace: 'account.profile' }),
    getTranslations({ locale, namespace: 'account' }),
    (await getServerApi()).GET('/api/v1/me/profile'),
  ]);
  if (!profile) throw new Error(`GET /api/v1/me/profile failed with status ${response.status}`);

  return (
    <div className="mx-auto flex max-w-[560px] flex-col gap-5 px-4 py-6 md:px-6">
      <Breadcrumb items={[{ label: tAccount('title'), href: '/account' }, { label: t('title') }]} />
      <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
      <ProfileForm
        profile={{
          displayName: profile.displayName ?? '',
          email: profile.email,
          phoneMasked: profile.phoneMasked,
          phoneChangeAllowed: profile.phoneChangeAllowed,
          preferredLocale: asLocale(profile.preferredLocale),
        }}
      />
    </div>
  );
}
