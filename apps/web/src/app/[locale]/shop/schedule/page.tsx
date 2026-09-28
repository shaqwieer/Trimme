import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { OpeningHoursCard, ProfessionalHoursCard } from '@/components/schedule/HoursEditors';
import { PauseCard } from '@/components/schedule/PauseCard';
import { BreaksCard, TimeOffCard } from '@/components/schedule/ScheduleEntries';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Hours, breaks and time off (s-hours, DV-A03/A04): the shop's weekly hours, each professional's hours, recurring and
 * one-off breaks, time off and closures with a conflict preview, and the online-booking pause. Staff see the schedule
 * read-only (`Shop.Schedule.Read`); owners edit it (`Shop.Schedule.Manage`, `Shop.OnlineBooking.Pause`).
 */
export default async function ShopSchedulePage({ params }: PageProps<'/[locale]/shop/schedule'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopSchedule' });

  return (
    <ShopFrame locale={locale} path="/shop/schedule" title={t('title')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Schedule.Read'))
          return <EmptyState icon="shield" title={t('title')} />;
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/shop/schedule');
        if (!data) return <EmptyState icon="shield" title={t('title')} />;

        const canManage = me.permissions.includes('Shop.Schedule.Manage');
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
