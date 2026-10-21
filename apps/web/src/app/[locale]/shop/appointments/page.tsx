import type { Metadata } from 'next';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { AppointmentsBoard } from '@/components/shop/board/AppointmentsBoard';
import { SimpleAppointments } from '@/components/shop/board/SimpleAppointments';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, SkeletonList } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The shop's appointments with the detail drawer (s-appointments, spec §13). The shop comes from the session. The owner
 * (who manages the shop's schedule) has the full board with filters and history; the desk has the simple list of
 * what is coming, at the client's request (D-134).
 */
export default async function ShopAppointmentsPage({ params }: PageProps<'/[locale]/shop/appointments'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBoard.appointments' });

  return (
    <ShopFrame
      locale={locale}
      path="/shop/appointments"
      title={(me) => (me.permissions.includes('Shop.Schedule.Manage') ? t('title') : t('simpleTitle'))}
    >
      {async (me, shop) => {
        if (shop.status === 'Suspended' || !me.permissions.includes('Shop.Bookings.Read')) {
          return <EmptyState icon="shield" title={t('title')} />;
        }
        const canUpdate = me.permissions.includes('Shop.Bookings.UpdateStatus');
        if (!me.permissions.includes('Shop.Schedule.Manage')) {
          return (
            <Suspense fallback={<SkeletonList rows={5} label={t('loading')} />}>
              <SimpleAppointments
                today={todayLocal(shop.timeZone)}
                timeZone={shop.timeZone}
                canUpdate={canUpdate}
              />
            </Suspense>
          );
        }
        const api = await getServerApi();
        const { data: professionals } = await api.GET('/api/v1/shop/professionals');
        return (
          <Suspense fallback={<SkeletonList rows={5} label={t('loading')} />}>
            <AppointmentsBoard
              today={todayLocal(shop.timeZone)}
              timeZone={shop.timeZone}
              professionals={(professionals ?? []).map((p) => ({
                id: p.id,
                nameAr: p.nameAr,
                nameEn: p.nameEn,
              }))}
              canUpdate={canUpdate}
            />
          </Suspense>
        );
      }}
    </ShopFrame>
  );
}
