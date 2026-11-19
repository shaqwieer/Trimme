import { getTranslations } from 'next-intl/server';
import { rebookHref } from '@/components/booking/BookingCards';
import { ButtonLink } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { type AppLocale, formatDate } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import type { components } from '@/lib/api/schema';

type CustomerBooking = components['schemas']['CustomerBookingResponse'];

/**
 * «احجز مثل آخر مرة» at the top of the customer's home (D-155, the client's 5-4: a customer cuts every six days or
 * so). The last completed visit (shop, barber, services) and «احجز نفسها», which opens the booking at the time step with
 * the same shop, barber and services. Drawn on the server; nothing when there is no completed visit.
 */
export async function LastVisitCard({ booking, locale }: { booking: CustomerBooking; locale: AppLocale }) {
  const t = await getTranslations({ locale, namespace: 'discover.lastVisit' });
  const shop = localizedName(locale, booking.shop.nameAr, booking.shop.nameEn);
  const barber = localizedName(locale, booking.professional.nameAr, booking.professional.nameEn);
  const service = localizedName(locale, booking.item.nameAr, booking.item.nameEn);
  return (
    <section
      aria-labelledby="last-visit-title"
      className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1"
      data-testid="last-visit"
    >
      <div className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
          <Icon name="refresh" className="size-5" />
        </span>
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <h2 id="last-visit-title" className="text-label font-bold text-navy-900">
            {t('title')}
          </h2>
          <p className="text-caption font-bold text-text-primary">{service}</p>
          <p className="text-helper text-text-secondary">
            {t('where', { shop, barber })} — {formatDate(booking.startsAt, locale, { withWeekday: false })}
          </p>
        </div>
      </div>
      <ButtonLink href={rebookHref(booking)} size="md" icon="calendar" className="self-start">
        {t('book')}
      </ButtonLink>
    </section>
  );
}
