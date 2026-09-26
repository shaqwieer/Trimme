import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopStatusBadge } from '@/components/admin/ShopStatusBadge';
import { SessionExpiryRedirect, SignOutButton } from '@/components/auth/SessionClient';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { ErrorState, InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { homeFor } from '@/lib/auth/paths';
import { requireUser } from '@/lib/auth/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Shop home. The shop comes from the session (GET /shop/me), never from the URL, so a shop user cannot open another
 * shop's dashboard. The operational overview arrives in Phase 13.
 */
export default async function ShopHomePage({ params }: PageProps<'/[locale]/shop'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const me = await requireUser(locale, '/shop');
  const t = await getTranslations({ locale: lang, namespace: 'shopHome' });

  if (me.userType !== 'ShopUser') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  const api = await getServerApi();
  const { data: shop } = await api.GET('/api/v1/shop/me');
  const name = shop ? (lang === 'ar' ? shop.nameAr : shop.nameEn) : '';

  return (
    <DashboardShell
      variant="shop"
      title={t('title')}
      permissions={shop?.status === 'Suspended' ? [] : me.permissions}
      sidebarFooter={
        <div className="flex flex-col gap-3 rounded-card bg-on-navy-subtle p-3">
          <p className="truncate text-label font-bold text-on-navy">{name}</p>
          <SignOutButton staff className="w-full" />
        </div>
      }
    >
      <SessionExpiryRedirect />
      {!shop ? (
        <ErrorState />
      ) : (
        <section className="flex max-w-[720px] flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-h2 font-bold text-navy-900" data-testid="shop-name">
              {name}
            </h2>
            <span className="sr-only">{t('statusLabel')}</span>
            <ShopStatusBadge status={shop.status} />
          </div>
          {shop.status === 'Suspended' && (
            <InlineAlert tone="danger" title={t('suspendedTitle')}>
              {t('suspendedBody')}
            </InlineAlert>
          )}
          {shop.status === 'Draft' && (
            <InlineAlert tone="warning" title={t('draftTitle')}>
              {t('draftBody')}
            </InlineAlert>
          )}
          <p className="text-body text-text-secondary">{t('body')}</p>
        </section>
      )}
    </DashboardShell>
  );
}
