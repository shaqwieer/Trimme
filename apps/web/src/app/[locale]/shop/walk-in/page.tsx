import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { WalkInFlow, type WalkInOffer } from '@/components/shop/board/WalkInFlow';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { EmptyState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { todayLocal } from '@/lib/i18n/localDate';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * Walk-in and phone bookings (s-walkin, spec §13), the barbers' (D-145, D-147): the shop's active services and packages
 * (online-bookable or not: the shop is not online), for the barber or a free colleague who does them, and their times
 * from the walk-in rules. The owner books none (D-146): the page answers «no access», like the API. The calendar can
 * prefill `professionalId`, `startsAt` and `date`.
 */
export default async function ShopWalkInPage({ params, searchParams }: PageProps<'/[locale]/shop/walk-in'>) {
  const [{ locale: raw }, query] = await Promise.all([params, searchParams]);
  const locale = asLocale(raw);
  const t = await getTranslations({ locale, namespace: 'shopBoard.walkIn' });

  return (
    <ShopFrame locale={locale} path="/shop/walk-in" title={t('title')}>
      {async (me, shop) => {
        if (shop.status === 'Suspended') return <EmptyState icon="shield" title={t('title')} />;
        if (!me.permissions.includes('Shop.Bookings.CreateWalkIn'))
          return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const [{ data: services }, { data: packages }] = await Promise.all([
          api.GET('/api/v1/shop/services'),
          api.GET('/api/v1/shop/packages'),
        ]);
        const offers: WalkInOffer[] = [
          ...(services ?? [])
            // Every service: a colleague may do what this barber does not (D-147); step 3 lists who does them all.
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
            })),
        ];
        const today = todayLocal(shop.timeZone);
        const date = firstParam(query.date);
        return (
          <WalkInFlow
            offers={offers}
            today={today}
            timeZone={shop.timeZone}
            initialProfessionalId={firstParam(query.professionalId) ?? me.professionalId ?? undefined}
            ownProfessionalId={me.professionalId ?? undefined}
            initialStart={firstParam(query.startsAt)}
            initialDate={date && /^\d{4}-\d{2}-\d{2}$/.test(date) && date >= today ? date : undefined}
          />
        );
      }}
    </ShopFrame>
  );
}
