import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { RescheduleForm } from '@/components/booking/RescheduleForm';
import { ButtonLink } from '@/components/ui/Button';
import { Breadcrumb } from '@/components/ui/data';
import { InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { requireCustomer } from '@/lib/auth/server';
import { getPublicShop } from '@/lib/discovery/shop-data';
import { OPERATING_TIME_ZONE } from '@/lib/i18n/config';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/bookings/[bookingId]/reschedule'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'bookings.reschedule' });
  return { title: t('title') };
}

/** Reschedule one of the customer's own bookings while the policy allows it (D-015, D-089, D-097). */
export default async function ReschedulePage({
  params,
}: PageProps<'/[locale]/account/bookings/[bookingId]/reschedule'>) {
  const { locale: raw, bookingId } = await params;
  const locale = asLocale(raw);
  const detail = `/account/bookings/${bookingId}`;
  const me = await requireCustomer(locale, `${detail}/reschedule`);
  if (!me) return <PermissionDenied homeHref="/" />;

  const api = await getServerApi();
  const { data: booking, response } = await api.GET('/api/v1/me/bookings/{bookingId}', {
    params: { path: { bookingId } },
  });
  if (response.status === 404 || response.status === 400) notFound();
  if (!booking) throw new Error(`GET /api/v1/me/bookings/{id} failed with status ${response.status}`);
  const [t, tList, shop] = await Promise.all([
    getTranslations({ locale, namespace: 'bookings.reschedule' }),
    getTranslations({ locale, namespace: 'bookings' }),
    getPublicShop(booking.shop.slug),
  ]);

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5 px-4 py-6 md:px-6">
      <Breadcrumb
        items={[
          { label: tList('title'), href: '/account/bookings' },
          { label: tList('detail.title'), href: detail },
          { label: t('title') },
        ]}
      />
      <h1 className="text-page-title font-bold text-navy-900">{t('title')}</h1>
      {booking.allowedActions.includes('Reschedule') ? (
        <RescheduleForm
          bookingId={booking.id}
          version={booking.version}
          startsAt={booking.startsAt}
          timeZone={shop?.timeZone ?? OPERATING_TIME_ZONE}
        />
      ) : (
        <InlineAlert
          tone="warning"
          title={t('notAllowed')}
          action={
            <ButtonLink href={detail} variant="outline" size="sm">
              {t('backToBooking')}
            </ButtonLink>
          }
        />
      )}
    </div>
  );
}
