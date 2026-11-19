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
 * each professional's hours, the shop's weekly breaks, time off and closures, and pauses online booking with a reason
 * and an optional return time (D-155: the owner's and the admin's, no longer the barbers'). A barber
 * (`Shop.Breaks.Manage`) sees nothing of that: their own day by the hour, where they set breaks with no reason and
 * mark themselves «غير متوفر اليوم» (D-134, D-145, D-155).
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
          // A barber never pauses the shop's booking (D-155, the client's 1-1): the owner and the admin do.
          const { data: professionals } = await api.GET('/api/v1/shop/professionals');
          return (
            <div className="flex max-w-[760px] flex-col gap-5">
              <BarberDayCard
                barbers={(professionals ?? [])
                  // A barber's login manages their own day only (D-145).
                  .filter((p) => p.status === 'Active' && (!me.professionalId || p.id === me.professionalId))
                  .map((p) => ({ id: p.id, nameAr: p.nameAr, nameEn: p.nameEn }))}
                today={today}
                own={Boolean(me.professionalId)}
              />
            </div>
          );
        }

        const [{ data }, { data: pause }] = await Promise.all([
          api.GET('/api/v1/shop/schedule'),
          api.GET('/api/v1/shop/online-booking'),
        ]);
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
              {canBreaks && (
                <BarberDayCard
                  barbers={data.professionals
                    .filter((p) => p.isActive)
                    .map((p) => ({ id: p.professionalId, nameAr: p.nameAr, nameEn: p.nameEn }))}
                  today={data.today}
                />
              )}
              {pause && <PauseCard state={pause} timeZone={shop.timeZone} canPause={canPause} />}
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
