import { getTranslations } from 'next-intl/server';
import { SignOutButton } from '@/components/auth/SessionClient';
import { Ltr } from '@/components/text/Ltr';
import { Icon } from '@/components/ui/icons';
import { PermissionDenied } from '@/components/ui/states';
import { Link, redirect } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { homeFor, withReturnTo } from '@/lib/auth/paths';
import { requireUser } from '@/lib/auth/server';

/** Customer account home (design c-profile, account card). Bookings and favorites arrive in Phase 12. */
export default async function AccountPage({ params }: PageProps<'/[locale]/account'>) {
  const { locale } = await params;
  const me = await requireUser(locale, '/account');
  const t = await getTranslations({ locale: locale === 'en' ? 'en' : 'ar', namespace: 'account' });

  if (me.userType !== 'Customer') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  if (!me.profileComplete) {
    redirect({ href: withReturnTo('/auth/complete-profile', '/account'), locale: asLocale(locale) });
  }

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-6 px-4 py-6 md:px-6">
      <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
      <dl className="grid gap-4 rounded-card border border-border bg-surface p-5 shadow-e1 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <dt className="text-helper text-text-tertiary">{t('name')}</dt>
          <dd className="text-body font-bold text-text-primary">{me.displayName}</dd>
        </div>
        <div className="flex flex-col gap-1">
          <dt className="text-helper text-text-tertiary">{t('phone')}</dt>
          <dd className="font-latin text-body font-semibold text-text-primary">
            <Ltr>{me.phoneMasked}</Ltr>
          </dd>
        </div>
      </dl>
      <Link
        href="/account/security"
        className="flex min-h-14 items-center gap-3 rounded-card border border-border bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle"
      >
        <Icon name="shield" className="size-5 text-brand-700" />
        <span className="flex-1">{t('security')}</span>
        <Icon name="chevL" className="size-4 text-text-tertiary" />
      </Link>
      <SignOutButton className="self-start" />
    </div>
  );
}
