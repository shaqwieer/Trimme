import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { BarberDayCard } from '@/components/schedule/BarberDayCard';
import { OpeningHoursCard, ProfessionalHoursCard } from '@/components/schedule/HoursEditors';
import { PauseCard } from '@/components/schedule/PauseCard';
import { BreaksCard, TimeOffCard } from '@/components/schedule/ScheduleEntries';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Hours, breaks and time off (s-hours, DV-A03/A04). The owner (`Shop.Schedule.Manage`) edits the shop's weekly hours,
 * each professional's hours, the shop's breaks, time off and closures, and pauses online booking. The desk
 * (`Shop.Breaks.Manage` only) sees nothing of that, at the client's request: just each barber's day by the hour, where
 * it sets breaks with no reason (D-134). The owner has the same day view too.
 */
export default async function ShopSchedulePage({ params }: PageProps<'/[locale]/shop/schedule'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopSchedule' });

  return (
    <ShopFrame
      locale={locale}
      path="/shop/schedule"
      title={(me) => (me.permissions.includes('Shop.Schedule.Manage') ? t('title') : t('staffTitle'))}
    >
      {async (me, shop) => {
        const canManage = me.permissions.includes('Shop.Schedule.Manage');
        const canBreaks = me.permissions.includes('Shop.Breaks.Manage');
        const api = await getServerApi();
        const today = todayLocal(shop.timeZone);

        if (!canManage) {
          if (!canBreaks) return <EmptyState icon="shield" title={t('title')} />;
          const { data: professionals } = await api.GET('/api/v1/shop/professionals');
          return (
            <div className="max-w-[760px]">
              <BarberDayCard
                barbers={(professionals ?? [])
                  .filter((p) => p.status === 'Active')
                  .map((p) => ({ id: p.id, nameAr: p.nameAr, nameEn: p.nameEn }))}
                today={today}
              />
            </div>
          );
        }

        const { data } = await api.GET('/api/v1/shop/schedule');
        if (!data) return <EmptyState icon="shield" title={t('title')} />;
        const canPause = me.permissions.includes('Shop.OnlineBooking.Pause');
        return (
          <div className="grid max-w-[1200px] items-start gap-5 lg:grid-cols-[1.4fr_1fr]">
            <div className="flex min-w-0 flex-col gap-5">
              <OpeningHoursCard
                intervals={data.openingHours.intervals}
                version={data.openingHours.version}
                canManage={canManage}
              />
              <ProfessionalHoursCard
                professionals={data.professionals}
                shopIntervals={data.openingHours.intervals}
                canManage={canManage}
              />
              <BreaksCard
                breaks={data.breaks}
                professionals={data.professionals}
                canManage={canManage}
                today={data.today}
              />
            </div>
            <div className="flex min-w-0 flex-col gap-5">
              <BarberDayCard
                barbers={data.professionals
                  .filter((p) => p.isActive)
                  .map((p) => ({ id: p.professionalId, nameAr: p.nameAr, nameEn: p.nameEn }))}
                today={data.today}
              />
              <PauseCard
                paused={data.onlineBookingPaused}
                pausedAt={data.onlineBookingPausedAt}
                canPause={canPause}
              />
              <TimeOffCard
                timeOff={data.timeOff}
                closures={data.closures}
                professionals={data.professionals}
                canManage={canManage}
                today={data.today}
              />
            </div>
          </div>
        );
      }}
    </ShopFrame>
  );
}
