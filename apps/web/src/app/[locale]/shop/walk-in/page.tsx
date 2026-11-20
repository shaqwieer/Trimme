import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { BarberDayCard } from '@/components/schedule/BarberDayCard';
import { WALK_IN_MODES, WalkInChooser, type WalkInMode } from '@/components/shop/board/WalkInChooser';
import { WalkInFlow, type WalkInOffer } from '@/components/shop/board/WalkInFlow';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * «حجز حضوري» (s-walkin, spec §13), the barbers' (D-145, D-147). Since D-158 (the client's request) it opens on four
 * choices — «زبون حضوري», «حجز بالتلفون», «بريك», «غير متوفر اليوم» — and shows the chosen one's screen below them:
 * - the walk-in or the phone booking: the shop's active services and packages (online-bookable or not: the shop is not
 *   online), for the barber or a free colleague who does them, and their times from the walk-in rules;
 * - the barber's breaks, or «غير متوفر اليوم», as on the hours page.
 * The owner books none (D-146): the page answers «no access», like the API. Older links still land on a screen: the
 * calendar's prefilled `professionalId`, `startsAt` and `date` open the walk-in (or the phone booking for a later day),
 * and `?kind=phone` the phone booking.
 */
export default async function ShopWalkInPage({ params, searchParams }: PageProps<'/[locale]/shop/walk-in'>) {
  const [{ locale: raw }, query] = await Promise.all([params, searchParams]);
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBoard.walkIn' });
  // «بريك» and «غير متوفر اليوم» are titled as themselves, not «حجز حضوري أو بالتلفون» (D-158).
  const requestedMode = firstParam(query.mode);
  const title =
    requestedMode === 'break' || requestedMode === 'unavailable' ? t(`choose.${requestedMode}`) : t('title');

  return (
    <ShopFrame locale={locale} path="/shop/walk-in" title={title}>
      {async (me, shop) => {
        if (shop.status === 'Suspended') return <EmptyState icon="shield" title={t('title')} />;
        if (!me.permissions.includes('Shop.Bookings.CreateWalkIn'))
          return <PermissionDenied homeHref="/shop" />;
        const today = todayLocal(shop.timeZone);
        const date = firstParam(query.date);
        const initialDate = date && /^\d{4}-\d{2}-\d{2}$/.test(date) && date >= today ? date : undefined;
        const requested = firstParam(query.mode);
        const mode: WalkInMode | undefined = WALK_IN_MODES.includes(requested as WalkInMode)
          ? (requested as WalkInMode)
          : firstParam(query.kind) === 'phone'
            ? 'phone'
            : firstParam(query.startsAt)
              ? initialDate && initialDate !== today
                ? 'phone'
                : 'walkIn'
              : undefined;

        const api = await getServerApi();
        let screen = null;
        if (mode === 'break' || mode === 'unavailable') {
          const { data: professionals } = await api.GET('/api/v1/shop/professionals');
          screen = (
            <BarberDayCard
              key={mode}
              section={mode === 'break' ? 'breaks' : 'unavailable'}
              barbers={(professionals ?? [])
                // A barber's login manages their own day only (D-145).
                .filter((p) => p.status === 'Active' && (!me.professionalId || p.id === me.professionalId))
                .map((p) => ({ id: p.id, nameAr: p.nameAr, nameEn: p.nameEn }))}
              today={today}
              own={Boolean(me.professionalId)}
            />
          );
        } else if (mode) {
          const [{ data: services }, { data: packages }] = await Promise.all([
            api.GET('/api/v1/shop/services'),
            api.GET('/api/v1/shop/packages'),
          ]);
          const offers: WalkInOffer[] = [
            ...(services ?? [])
              // Every service: a colleague may do what this barber does not (D-147); the barber step lists who does them all.
              .filter((s) => s.isActive && !s.isArchived && s.moderation === 'Visible')
              .map((s) => ({
                kind: 'service' as const,
                id: s.id,
                nameAr: s.nameAr,
                nameEn: s.nameEn ?? null,
                price: s.price,
                currency: s.currency,
                durationMinutes: s.durationMinutes,
              })),
            ...(packages ?? [])
              .filter((p) => p.isActive && !p.isArchived && p.isBookable && p.moderation === 'Visible')
              .map((p) => ({
                kind: 'package' as const,
                id: p.id,
                nameAr: p.nameAr,
                nameEn: p.nameEn ?? null,
                price: p.price,
                currency: p.currency,
                durationMinutes: p.durationMinutes,
                itemIds: p.items.map((item) => item.serviceId),
              })),
          ];
          const kind = mode === 'phone' ? 'Phone' : 'WalkIn';
          screen = (
            <WalkInFlow
              key={kind}
              kind={kind}
              offers={offers}
              today={today}
              timeZone={shop.timeZone}
              initialProfessionalId={firstParam(query.professionalId) ?? me.professionalId ?? undefined}
              ownProfessionalId={me.professionalId ?? undefined}
              initialStart={firstParam(query.startsAt)}
              initialDate={kind === 'Phone' ? initialDate : undefined}
            />
          );
        }

        return (
          <div className="flex flex-col gap-5">
            <div className="max-w-[760px]">
              <WalkInChooser current={mode} />
            </div>
            {screen && (
              <div className={mode === 'break' || mode === 'unavailable' ? 'max-w-[760px]' : undefined}>
                {screen}
              </div>
            )}
          </div>
        );
      }}
    </ShopFrame>
  );
}
