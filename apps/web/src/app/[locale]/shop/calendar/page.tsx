import type { Metadata } from 'next';
import { Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { ShopCalendar } from '@/components/shop/board/ShopCalendar';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, SkeletonList } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Day and week calendar (s-calendar, spec §13, D-034). The shop comes from the session. */
export default async function ShopCalendarPage({ params }: PageProps<'/[locale]/shop/calendar'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBoard.calendar' });

  return (
    <ShopFrame locale={locale} path="/shop/calendar" title={t('title')}>
      {(me, shop) =>
        shop.status === 'Suspended' || !me.permissions.includes('Shop.Bookings.Read') ? (
          <EmptyState icon="shield" title={t('title')} />
        ) : (
          <Suspense fallback={<SkeletonList rows={6} label={t('loading')} />}>
            <ShopCalendar
              today={todayLocal(shop.timeZone)}
              timeZone={shop.timeZone}
              canWalkIn={me.permissions.includes('Shop.Bookings.CreateWalkIn')}
              canUpdate={me.permissions.includes('Shop.Bookings.UpdateStatus')}
            />
          </Suspense>
        )
      }
    </ShopFrame>
  );
}
