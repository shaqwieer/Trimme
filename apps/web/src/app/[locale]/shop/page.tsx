import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { SessionExpiryRedirect, SignOutButton } from '@/components/auth/SessionClient';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { PermissionDenied } from '@/components/ui/states';
import { homeFor } from '@/lib/auth/paths';
import { requireUser } from '@/lib/auth/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Shop home. Shop accounts arrive with tenancy in Phase 05; the operational overview in Phase 13. */
export default async function ShopHomePage({ params }: PageProps<'/[locale]/shop'>) {
  const { locale } = await params;
  const me = await requireUser(locale, '/shop');
  const t = await getTranslations({ locale: locale === 'en' ? 'en' : 'ar', namespace: 'shopHome' });

  if (me.userType !== 'ShopUser') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  return (
    <DashboardShell
      variant="shop"
      title={t('title')}
      permissions={me.permissions}
      sidebarFooter={<SignOutButton staff className="w-full" />}
    >
      <SessionExpiryRedirect />
      <p className="max-w-[60ch] text-body text-text-secondary">{t('body')}</p>
    </DashboardShell>
  );
}
