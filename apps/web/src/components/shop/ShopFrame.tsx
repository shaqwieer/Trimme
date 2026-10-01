import type { ReactNode } from 'react';
import { SessionExpiryRedirect, SignOutButton } from '@/components/auth/SessionClient';
import { QueryProvider } from '@/components/providers/QueryProvider';
import { DashboardShell } from '@/components/shell/DashboardShell';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { homeFor } from '@/lib/auth/paths';
import { type Me, requireUser } from '@/lib/auth/server';
import { LiveIndicator, OperationsLiveProvider } from './live/OperationsLive';
import { ShopBanners } from './ShopBanners';

type ShopFrameProps = {
  locale: string;
  /** This page's locale-less path, used as `returnTo` when the session must be restored. */
  path: string;
  title: string;
  children: (me: Me, shop: { status: string; name: string; timeZone: string }) => ReactNode;
};

/**
 * Server guard + shop dashboard shell. The shop comes from the session (`GET /shop/me`), never from the URL; while
 * the shop is suspended the navigation is empty and shop data endpoints answer 404 (D-061). An operable shop gets the
 * pause and subscription banners on every page (the subscription page shows its own), and — with `Shop.Bookings.Read`
 * — the live connection (D-099).
 */
export async function ShopFrame({ locale, path, title, children }: ShopFrameProps) {
  const me = await requireUser(locale, path);
  if (me.userType !== 'ShopUser') {
    return <PermissionDenied homeHref={homeFor(me.userType)} />;
  }

  const api = await getServerApi();
  // One round of reads: the shop, its pause state and (with the permission) its subscription.
  const [{ data: shop }, pause, subscription] = await Promise.all([
    api.GET('/api/v1/shop/me'),
    api.GET('/api/v1/shop/online-booking').then((r) => r.data ?? null),
    me.permissions.includes('Shop.Subscription.Read') && path !== '/shop/subscription'
      ? api.GET('/api/v1/shop/subscription').then((r) => r.data ?? null)
      : Promise.resolve(null),
  ]);
  const name = shop ? (asLocale(locale) === 'ar' ? shop.nameAr : shop.nameEn) : '';
  const status = shop?.status ?? 'Suspended';
  const operable = Boolean(shop) && status !== 'Suspended';
  const live = operable && me.permissions.includes('Shop.Bookings.Read');

  const content = (
    <>
      <SessionExpiryRedirect />
      <div className="flex flex-col gap-5">
        {operable && <ShopBanners paused={pause?.paused ?? false} subscription={subscription} />}
        {children(me, { status, name, timeZone: shop?.timeZone ?? 'Asia/Riyadh' })}
      </div>
    </>
  );

  return (
    <QueryProvider>
      {live ? (
        <OperationsLiveProvider>
          <Shell
            title={title}
            name={name}
            permissions={operable ? me.permissions : []}
            addon={<LiveIndicator />}
          >
            {content}
          </Shell>
        </OperationsLiveProvider>
      ) : (
        <Shell title={title} name={name} permissions={operable ? me.permissions : []}>
          {content}
        </Shell>
      )}
    </QueryProvider>
  );
}

function Shell({
  title,
  name,
  permissions,
  addon,
  children,
}: {
  title: string;
  name: string;
  permissions: readonly string[];
  addon?: ReactNode;
  children: ReactNode;
}) {
  return (
    <DashboardShell
      variant="shop"
      title={title}
      titleAddon={addon}
      permissions={permissions}
      notifications={permissions.includes('Shop.Bookings.Read')}
      sidebarFooter={
        <div className="flex flex-col gap-3 rounded-card bg-on-chrome-subtle p-3">
          <p className="truncate text-label font-bold text-on-chrome">{name}</p>
          <SignOutButton staff className="w-full" />
        </div>
      }
    >
      {children}
    </DashboardShell>
  );
}
