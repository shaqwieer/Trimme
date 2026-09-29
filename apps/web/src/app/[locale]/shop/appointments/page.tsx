import type { Metadata } from 'next';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { AppointmentsBoard } from '@/components/shop/board/AppointmentsBoard';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, SkeletonList } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** The shop's appointments with the detail drawer (s-appointments, spec §13). The shop comes from the session. */
export default async function ShopAppointmentsPage({ params }: PageProps<'/[locale]/shop/appointments'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBoard.appointments' });

  return (
    <ShopFrame locale={locale} path="/shop/appointments" title={t('title')}>
      {async (me, shop) => {
        if (shop.status === 'Suspended' || !me.permissions.includes('Shop.Bookings.Read')) {
          return <EmptyState icon="shield" title={t('title')} />;
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
              canUpdate={me.permissions.includes('Shop.Bookings.UpdateStatus')}
            />
          </Suspense>
        );
      }}
    </ShopFrame>
  );
}
