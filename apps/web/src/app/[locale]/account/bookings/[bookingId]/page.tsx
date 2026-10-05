import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import type { ReactNode } from 'react';
import { getTranslations } from 'next-intl/server';
import { isActive, rebookHref } from '@/components/booking/BookingCards';
import { CancelBookingButton, Countdown } from '@/components/booking/BookingDetailClient';
import { BookingPolicy, CreatedBanner } from '@/components/booking/BookingDetailParts';
import { Ltr } from '@/components/text/Ltr';
import { StatusBadge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { Breadcrumb } from '@/components/ui/data';
import { Icon, type DesignIconName } from '@/components/ui/icons';
import { InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { requireCustomer } from '@/lib/auth/server';
import { directionsUrl } from '@/lib/booking/links';
import { getPublicShop } from '@/lib/discovery/shop-data';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatPrice,
  formatRating,
  formatTime,
} from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export async function generateMetadata({
  params,
}: PageProps<'/[locale]/account/bookings/[bookingId]'>): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'bookings.detail' });
  return { title: t('title') };
}

function Row({ icon, label, children }: { icon: DesignIconName; label: string; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3 py-3">
      <Icon name={icon} className="size-[18px] shrink-0 text-brand-700" />
      <dt className="text-helper text-text-secondary">{label}</dt>
      <dd className="ms-auto text-end text-label font-bold text-text-primary">{children}</dd>
    </div>
  );
}

/**
 * One appointment (c-appointments 1742–1789): status, date and time with a countdown, add to calendar and directions,
 * the booked service's snapshot (price paid at the shop), the reference, the cancellation policy, and the actions the
 * API allows (cancel, reschedule, rate). After the cutoff the page says so and gives the shop's phone instead.
 */
export default async function BookingDetailPage({
  params,
  searchParams,
}: PageProps<'/[locale]/account/bookings/[bookingId]'>) {
  const [{ locale: raw, bookingId }, query] = await Promise.all([params, searchParams]);
  const locale = asLocale(raw) as AppLocale;
  const here = `/account/bookings/${bookingId}`;
  const me = await requireCustomer(locale, here);
  if (!me) return <PermissionDenied homeHref="/" />;

  const api = await getServerApi();
  const { data: booking, response } = await api.GET('/api/v1/me/bookings/{bookingId}', {
    params: { path: { bookingId } },
  });
  if (response.status === 404 || response.status === 400) notFound();
  if (!booking) throw new Error(`GET /api/v1/me/bookings/{id} failed with status ${response.status}`);

  const [t, tList, shop] = await Promise.all([
    getTranslations({ locale, namespace: 'bookings.detail' }),
    getTranslations({ locale, namespace: 'bookings' }),
    getPublicShop(booking.shop.slug),
  ]);
  const timeZone = shop?.timeZone;
  const actions = new Set(booking.allowedActions);
  const active = isActive(booking);
  const service = localizedName(locale, booking.item.nameAr, booking.item.nameEn);
  const shopName = localizedName(locale, booking.shop.nameAr, booking.shop.nameEn);
  const when = `${formatDate(booking.startsAt, locale, { timeZone })} ${formatTime(booking.startsAt, locale, timeZone)}`;
  const created = firstParam(query.created) === '1';

  return (
    <div className="mx-auto flex max-w-[720px] flex-col gap-5 px-4 py-6 md:px-6" data-testid="booking-detail">
      <Breadcrumb items={[{ label: tList('title'), href: '/account/bookings' }, { label: t('title') }]} />

      {created && <CreatedBanner status={booking.status} />}
      {firstParam(query.rescheduled) === '1' && <InlineAlert tone="success" title={t('rescheduled')} />}
      {firstParam(query.cancelled) === '1' && <InlineAlert tone="success" title={t('cancelled')} />}
      {firstParam(query.reviewed) === '1' && <InlineAlert tone="success" title={t('reviewed')} />}

      <section
        aria-labelledby="booking-when"
        className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
      >
        <StatusBadge kind="booking" status={booking.status} className="self-start" />
        <p className="text-label font-bold text-text-secondary">
          {formatDate(booking.startsAt, locale, { timeZone })}
        </p>
        <h1 id="booking-when" className="text-[1.75rem] leading-tight font-extrabold text-navy-900">
          {formatTime(booking.startsAt, locale, timeZone)} — {formatTime(booking.endsAt, locale, timeZone)}
        </h1>
        {active && <Countdown startsAt={booking.startsAt} timeZone={timeZone} />}
        {active && (
          <div className="flex flex-wrap gap-2">
            <a
              href={`/api/v1/me/bookings/${booking.id}/calendar.ics`}
              download={`${booking.reference}.ics`}
              className="inline-flex min-h-11 items-center gap-2 rounded-button border border-border-input bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle"
            >
              <Icon name="calendar" className="size-4" />
              {t('addToCalendar')}
            </a>
            {shop?.location && (
              <a
                href={directionsUrl(shop.location.latitude, shop.location.longitude)}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex min-h-11 items-center gap-2 rounded-button border border-border-input bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle"
              >
                <Icon name="pin" className="size-4" />
                {t('directions')}
              </a>
            )}
          </div>
        )}
      </section>

      <dl className="flex flex-col divide-y divide-border-row rounded-card border border-border bg-surface px-4">
        <Row icon="store" label={t('rows.shop')}>
          {shopName}
        </Row>
        <Row icon="scissors" label={t('rows.service')}>
          {service}
        </Row>
        <Row icon="user" label={t('rows.professional')}>
          {localizedName(locale, booking.professional.nameAr, booking.professional.nameEn)}
        </Row>
        <Row icon="clock" label={t('rows.duration')}>
          {formatDurationMinutes(booking.item.durationMinutes, locale)}
        </Row>
        <Row icon="qr" label={t('rows.reference')}>
          <Ltr>{booking.reference}</Ltr>
        </Row>
        {booking.note && (
          <Row icon="msg" label={t('rows.note')}>
            {booking.note}
          </Row>
        )}
        {booking.cancellationReason && (
          <Row icon="info" label={t('rows.cancellationReason')}>
            {booking.cancellationReason}
          </Row>
        )}
        <div className="flex items-center justify-between py-3.5">
          <dt className="text-label font-bold text-text-strong">{t('total')}</dt>
          <dd className="font-latin text-[1.125rem] font-extrabold text-navy-900">
            {formatPrice(booking.item.price, locale, booking.item.currency)}
          </dd>
        </div>
      </dl>

      {active && (
        <BookingPolicy
          startsAt={booking.startsAt}
          cutoffMinutes={booking.cancellationCutoffMinutes}
          canChange={actions.has('Cancel')}
          timeZone={timeZone}
        />
      )}

      {booking.reviewRating != null && (
        <p className="flex items-center gap-2 text-label font-bold text-success-700">
          <Icon name="star" className="size-4 fill-rating text-rating" />
          {tList('actions.rated', { rating: formatRating(booking.reviewRating, locale) })}
        </p>
      )}

      <div className="flex flex-col gap-2.5 sm:flex-row">
        {actions.has('Reschedule') && (
          <ButtonLink href={`${here}/reschedule`} variant="outline" fullWidth icon="refresh">
            {tList('actions.reschedule')}
          </ButtonLink>
        )}
        {actions.has('Cancel') && (
          <div className="flex-1">
            <CancelBookingButton bookingId={booking.id} version={booking.version} when={when} />
          </div>
        )}
        {actions.has('Review') && (
          <ButtonLink href={`${here}/review`} variant="primary" fullWidth icon="star">
            {tList('actions.review')}
          </ButtonLink>
        )}
        {!active && (
          <ButtonLink href={rebookHref(booking)} variant="outline" fullWidth icon="refresh">
            {tList('actions.rebook')}
          </ButtonLink>
        )}
      </div>
    </div>
  );
}
