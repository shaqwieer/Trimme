import type { ReactNode } from 'react';
import { SessionExpiryRedirect, SignOutButton } from '@/components/auth/SessionClient';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { homeFor } from '@/lib/auth/paths';
import { type Me, requireUser } from '@/lib/auth/server';

type ShopFrameProps = {
  locale: string;
  /** This page's locale-less path, used as `returnTo` when the session must be restored. */
  path: string;
  title: string;
  children: (me: Me, shop: { status: string; name: string }) => ReactNode;
};

/**
 * Server guard + shop dashboard shell. The shop comes from the session (`GET /shop/me`), never from the URL; while
 * the shop is suspended the navigation is empty and shop data endpoints answer 404 (D-061).
 */
export async function ShopFrame({ locale, path, title, children }: ShopFrameProps) {
  const me = await requireUser(locale, path);
  if (me.userType !== 'ShopUser') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  const api = await getServerApi();
  const { data: shop } = await api.GET('/api/v1/shop/me');
  const name = shop ? (asLocale(locale) === 'ar' ? shop.nameAr : shop.nameEn) : '';
  const status = shop?.status ?? 'Suspended';

  return (
    <DashboardShell
      variant="shop"
      title={title}
      permissions={status === 'Suspended' ? [] : me.permissions}
      sidebarFooter={
        <div className="flex flex-col gap-3 rounded-card bg-on-navy-subtle p-3">
          <p className="truncate text-label font-bold text-on-navy">{name}</p>
          <SignOutButton staff className="w-full" />
        </div>
      }
    >
      <SessionExpiryRedirect />
      {children(me, { status, name })}
    </DashboardShell>
  );
}
