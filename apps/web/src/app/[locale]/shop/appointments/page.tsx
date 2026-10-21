import type { Metadata } from 'next';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { SimpleAppointments } from '@/components/shop/board/SimpleAppointments';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, SkeletonList } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The shop's appointments with the detail drawer (s-appointments, spec §13). The shop comes from the session. At the
 * client's request the desk (D-134) and the owner (D-136) have the same simple list of what is coming; the owner also
 * sees whether each customer is new or returning (D-135). Past visits stay in the calendar; the overview has the figures.
 */
export default async function ShopAppointmentsPage({ params }: PageProps<'/[locale]/shop/appointments'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBoard.appointments' });

  return (
    <ShopFrame locale={locale} path="/shop/appointments" title={t('title')}>
      {(me, shop) => {
        if (shop.status === 'Suspended' || !me.permissions.includes('Shop.Bookings.Read')) {
          return <EmptyState icon="shield" title={t('title')} />;
        }
        return (
          <Suspense fallback={<SkeletonList rows={5} label={t('loading')} />}>
            <SimpleAppointments
              today={todayLocal(shop.timeZone)}
              timeZone={shop.timeZone}
              canUpdate={me.permissions.includes('Shop.Bookings.UpdateStatus')}
            />
          </Suspense>
        );
      }}
    </ShopFrame>
  );
}
