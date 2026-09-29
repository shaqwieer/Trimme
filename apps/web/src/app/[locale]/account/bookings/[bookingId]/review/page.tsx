import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations } from 'next-intl/server';
import { ReviewForm } from '@/components/booking/ReviewForm';
import { Avatar } from '@/components/ui/Avatar';
import { ButtonLink } from '@/components/ui/Button';
import { Breadcrumb } from '@/components/ui/data';
import { InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { requireCustomer } from '@/lib/auth/server';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/bookings/[bookingId]/review'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'review' });
  return { title: t('title') };
}

/** Rate a completed visit, once and within the review window (R-CUS-09, D-017, D-097). */
export default async function ReviewPage({
  params,
}: PageProps<'/[locale]/account/bookings/[bookingId]/review'>) {
  const { locale: raw, bookingId } = await params;
  const locale = asLocale(raw) as AppLocale;
  const detail = `/account/bookings/${bookingId}`;
  const me = await requireCustomer(locale, `${detail}/review`);
  if (!me) return <PermissionDenied homeHref="/" />;

  const api = await getServerApi();
  const { data: booking, response } = await api.GET('/api/v1/me/bookings/{bookingId}', {
    params: { path: { bookingId } },
  });
  if (response.status === 404 || response.status === 400) notFound();
  if (!booking) throw new Error(`GET /api/v1/me/bookings/{id} failed with status ${response.status}`);
  const [t, tList] = await Promise.all([
    getTranslations({ locale, namespace: 'review' }),
    getTranslations({ locale, namespace: 'bookings' }),
  ]);

  const professional = localizedName(locale, booking.professional.nameAr, booking.professional.nameEn);
  const reason =
    booking.reviewRating != null
      ? 'reviewed'
      : booking.status !== 'Completed'
        ? 'notCompleted'
        : booking.allowedActions.includes('Review')
          ? null
          : 'closed';

  return (
    <div className="mx-auto flex max-w-[560px] flex-col gap-5 px-4 py-6 md:px-6">
      <Breadcrumb
        items={[
          { label: tList('title'), href: '/account/bookings' },
          { label: tList('detail.title'), href: detail },
          { label: t('title') },
        ]}
      />
      <div className="flex flex-col items-center gap-2 text-center">
        <Avatar name={professional} size="xl" />
        <h1 className="text-[1.25rem] font-bold text-navy-900">
          {t('heading', { name: professional.split(' ')[0] ?? professional })}
        </h1>
        <p className="text-helper text-text-secondary">
          {localizedName(locale, booking.item.nameAr, booking.item.nameEn)} ·{' '}
          {localizedName(locale, booking.shop.nameAr, booking.shop.nameEn)} ·{' '}
          {formatDate(booking.startsAt, locale)} {formatTime(booking.startsAt, locale)}
        </p>
      </div>
      {reason ? (
        <InlineAlert
          tone={reason === 'reviewed' ? 'success' : 'warning'}
          title={t(`unavailable.${reason}`)}
          action={
            <ButtonLink href={detail} variant="outline" size="sm">
              {t('backToBooking')}
            </ButtonLink>
          }
        />
      ) : (
        <ReviewForm bookingId={booking.id} />
      )}
    </div>
  );
}
