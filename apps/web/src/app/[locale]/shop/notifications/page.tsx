import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { NotificationsPanel } from '@/components/notifications/NotificationsPanel';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The shop's notification centre (spec §13, DV-A05, R-SD-08, D-112): what customers, barbers and the platform did with
 * the shop's bookings, and subscription warnings, for the owner. A barber's login sees its own notices instead: their
 * bookings only (D-145). Never a customer phone number.
 */
export default async function ShopNotificationsPage({ params }: PageProps<'/[locale]/shop/notifications'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'notifications' });

  return (
    <ShopFrame locale={locale} path="/shop/notifications" title={t('title')}>
      {async (me, shop) => {
        if (shop.status === 'Suspended' || !me.permissions.includes('Shop.Bookings.Read')) {
          return <EmptyState icon="shield" title={t('title')} />;
        }
        const api = await getServerApi();
        const owner = me.permissions.includes('Shop.Notifications.Manage');
        const query = { params: { query: { pageSize: 50 } } };
        const { data } = owner
          ? await api.GET('/api/v1/shop/notifications', query)
          : await api.GET('/api/v1/me/notifications', query);
        if (!data) return <ErrorState />;
        return (
          <div className="mx-auto w-full max-w-[760px]">
            <NotificationsPanel
              audience={owner ? 'shop' : 'barber'}
              items={data.items}
              unread={data.unread}
            />
          </div>
        );
      }}
    </ShopFrame>
  );
}
