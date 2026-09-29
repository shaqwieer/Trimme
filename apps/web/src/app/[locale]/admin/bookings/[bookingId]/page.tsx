import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { BookingIntervention } from '@/components/admin/ops/BookingIntervention';
import { StatusBadge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/cards';
import { Breadcrumb, Timeline, type TimelineItem } from '@/components/ui/data';
import { ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatDate, formatDurationMinutes, formatPrice, formatTime } from '@/lib/i18n/format';
import { todayLocal } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * One booking for the platform team (DV-A09, R-AD-05): what was booked (the snapshot), who and where, the full history
 * with actors and reasons, the shop's internal notes (read-only here), and the intervention panel. No phone number is
 * part of the booking; the customer's profile page has the audited reveal.
 */
export default async function AdminBookingPage({
  params,
}: PageProps<'/[locale]/admin/bookings/[bookingId]'>) {
  const { locale, bookingId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminBookings' });
  const tDrawer = await getTranslations({ locale: lang, namespace: 'shopBoard.drawer' });
  const tStatus = await getTranslations({ locale: lang, namespace: 'status.booking' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/bookings/${bookingId}`}
      title={t('detailTitle')}
      permission="Admin.Bookings.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data, response } = await api.GET('/api/v1/admin/bookings/{bookingId}', {
          params: { path: { bookingId } },
        });
        if (!data) return <ErrorState title={response.status === 404 ? t('notFound') : undefined} />;
        const { booking: detail, notes } = data;
        const { booking, shop } = detail;
        const canIntervene = me.permissions.includes('Admin.Bookings.Intervene');
        const history: TimelineItem[] = detail.history.map((h, index) => ({
          id: `${index}`,
          tone:
            h.kind === 'Rescheduled'
              ? 'warning'
              : h.toStatus.startsWith('Cancelled')
                ? 'danger'
                : h.toStatus === 'Completed'
                  ? 'success'
                  : 'brand',
          title:
            h.kind === 'Created'
              ? tDrawer('history.created')
              : h.kind === 'Rescheduled'
                ? t('history.rescheduled', {
                    from: `${formatDate(h.previousStartsAt ?? '', lang, { withWeekday: false })} ${formatTime(h.previousStartsAt ?? '', lang)}`,
                  })
                : tDrawer('history.status', { status: tStatus(h.toStatus) }),
          meta: (
            <>
              {tDrawer(`actor.${h.actorType}`)} · {formatDate(h.occurredAt, lang, { withWeekday: false })}{' '}
              {formatTime(h.occurredAt, lang)}
              {h.reason && (
                <span className="block text-text-secondary">{t('history.reason', { reason: h.reason })}</span>
              )}
            </>
          ),
        }));

        return (
          <div className="flex flex-col gap-4">
            <Breadcrumb
              items={[{ label: t('title'), href: '/admin/bookings' }, { label: booking.reference }]}
            />
            <div className="grid gap-4 lg:grid-cols-[1.4fr_1fr]">
              <Card as="section" className="flex flex-col gap-4 p-5">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <h2 className="text-h3 font-bold text-navy-900">
                    <span dir="ltr" className="font-latin">
                      {booking.reference}
                    </span>
                  </h2>
                  <StatusBadge kind="booking" status={booking.status} />
                </div>
                <dl className="grid gap-x-6 gap-y-3 text-caption sm:grid-cols-2">
                  <Row label={tDrawer('when')}>
                    {formatDate(booking.startsAt, lang, { withYear: true })} ·{' '}
                    {formatTime(booking.startsAt, lang)} – {formatTime(booking.endsAt, lang)}
                  </Row>
                  <Row label={tDrawer('rows.customer')}>
                    {detail.customerId && me.permissions.includes('Admin.Customers.View') ? (
                      <Link
                        href={`/admin/customers/${detail.customerId}`}
                        className="font-bold text-brand-700 hover:underline"
                      >
                        {booking.customerName}
                      </Link>
                    ) : (
                      booking.customerName
                    )}
                  </Row>
                  <Row label={t('rows.shop')}>
                    <Link
                      href={`/admin/shops/${shop.id}`}
                      className="font-bold text-brand-700 hover:underline"
                    >
                      {localizedName(lang, shop.nameAr, shop.nameEn)}
                    </Link>
                  </Row>
                  <Row label={tDrawer('rows.professional')}>
                    {localizedName(lang, booking.professional.nameAr, booking.professional.nameEn)}
                  </Row>
                  <Row label={tDrawer('rows.service')}>
                    {localizedName(lang, booking.item.nameAr, booking.item.nameEn)} ·{' '}
                    {formatDurationMinutes(booking.item.durationMinutes, lang)}
                  </Row>
                  <Row label={tDrawer('rows.amount')}>
                    {tDrawer('amount', {
                      price: formatPrice(booking.item.price, lang, booking.item.currency),
                    })}
                  </Row>
                  <Row label={tDrawer('rows.source')}>{tDrawer(`channel.${booking.channel}`)}</Row>
                  {booking.note && <Row label={tDrawer('rows.note')}>{booking.note}</Row>}
                  {booking.cancellationReason && (
                    <Row label={tDrawer('rows.cancellationReason')}>{booking.cancellationReason}</Row>
                  )}
                </dl>
                <p className="rounded-button bg-bg-subtle p-3 text-helper text-text-secondary">
                  {t('noPhone')}
                </p>
              </Card>

              <div className="flex flex-col gap-4">
                {canIntervene && (
                  <Card as="section" className="flex flex-col gap-3 p-5">
                    <h2 className="text-h3 font-bold text-navy-900">{t('intervene.heading')}</h2>
                    <BookingIntervention
                      bookingId={booking.id}
                      version={booking.version}
                      allowed={booking.allowedTransitions}
                      canReschedule={booking.status === 'Pending' || booking.status === 'Confirmed'}
                      today={todayLocal()}
                      timeZone="Asia/Riyadh"
                    />
                  </Card>
                )}
                <Card as="section" className="flex flex-col gap-3 p-5">
                  <h2 className="text-h3 font-bold text-navy-900">{tDrawer('history.title')}</h2>
                  <Timeline items={history} />
                </Card>
                <Card as="section" className="flex flex-col gap-3 p-5">
                  <h2 className="text-h3 font-bold text-navy-900">{tDrawer('notes.title')}</h2>
                  {notes.length === 0 ? (
                    <p className="text-caption text-text-secondary">{t('noNotes')}</p>
                  ) : (
                    <ul className="flex flex-col gap-2">
                      {notes.map((note) => (
                        <li
                          key={note.id}
                          className="rounded-button bg-bg-subtle p-3 text-caption text-text-strong"
                        >
                          {note.text}
                          <span className="mt-1 block text-helper text-text-tertiary">
                            {formatDate(note.createdAt, lang, { withWeekday: false })}{' '}
                            {formatTime(note.createdAt, lang)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  )}
                </Card>
              </div>
            </div>
          </div>
        );
      }}
    </AdminFrame>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-helper text-text-tertiary">{label}</dt>
      <dd className="font-semibold text-text-primary">{children}</dd>
    </div>
  );
}
