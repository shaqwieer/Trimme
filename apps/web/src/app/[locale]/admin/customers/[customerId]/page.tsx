import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { CustomerContact } from '@/components/admin/ops/CustomerContact';
import { Avatar } from '@/components/ui/Avatar';
import { StatusBadge } from '@/components/ui/Badge';
import { Card, KpiTile } from '@/components/ui/cards';
import { Breadcrumb } from '@/components/ui/data';
import { ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import type { components } from '@/lib/api/schema';
import { getServerApi } from '@/lib/api/server';
import { formatDate, formatNumber, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const LIST_SIZE = 10;

type Row = components['schemas']['AdminBookingResponse'];

/**
 * A customer's profile for support (a-appointments side card promoted to a page, DV-A08, DV-S17): registration date,
 * booking figures, upcoming and previous bookings, and the mobile masked with an audited, permission-gated reveal.
 */
export default async function AdminCustomerPage({
  params,
}: PageProps<'/[locale]/admin/customers/[customerId]'>) {
  const { locale, customerId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminCustomers' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/customers/${customerId}`}
      title={t('detailTitle')}
      permission="Admin.Customers.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const now = new Date().toISOString();
        const canBookings = me.permissions.includes('Admin.Bookings.View');
        const [{ data: customer, response }, upcoming, previous] = await Promise.all([
          api.GET('/api/v1/admin/customers/{customerId}', { params: { path: { customerId } } }),
          canBookings
            ? api.GET('/api/v1/admin/bookings', {
                params: {
                  query: {
                    customerId,
                    from: now,
                    status: ['Pending', 'Confirmed', 'Arrived'],
                    sort: 'asc',
                    pageSize: LIST_SIZE,
                  },
                },
              })
            : Promise.resolve(undefined),
          canBookings
            ? api.GET('/api/v1/admin/bookings', {
                params: { query: { customerId, to: now, pageSize: LIST_SIZE } },
              })
            : Promise.resolve(undefined),
        ]);
        if (!customer) return <ErrorState title={response.status === 404 ? t('notFound') : undefined} />;
        const name = customer.displayName ?? t('noName');
        const list = (rows: readonly Row[] | undefined, empty: string) =>
          !rows || rows.length === 0 ? (
            <p className="text-caption text-text-secondary">{empty}</p>
          ) : (
            <ul className="flex flex-col divide-y divide-border-row">
              {rows.map((b) => (
                <li key={b.booking.id} className="flex items-center justify-between gap-3 py-2.5">
                  <div className="min-w-0">
                    <Link
                      href={`/admin/bookings/${b.booking.id}`}
                      className="font-bold text-text-primary hover:underline"
                    >
                      {localizedName(lang, b.booking.item.nameAr, b.booking.item.nameEn)}
                    </Link>
                    <p className="text-helper text-text-secondary">
                      {formatDate(b.booking.startsAt, lang, { withWeekday: false })} ·{' '}
                      {formatTime(b.booking.startsAt, lang)} ·{' '}
                      {localizedName(lang, b.shop.nameAr, b.shop.nameEn)}
                    </p>
                  </div>
                  <StatusBadge kind="booking" status={b.booking.status} size="sm" />
                </li>
              ))}
            </ul>
          );

        return (
          <div className="flex flex-col gap-4">
            <Breadcrumb items={[{ label: t('title'), href: '/admin/customers' }, { label: name }]} />
            <Card as="section" className="flex flex-col gap-4 p-5">
              <div className="flex flex-wrap items-center gap-4">
                <Avatar name={name} size="lg" />
                <div className="min-w-0 flex-1">
                  <h2 className="text-h3 font-bold text-navy-900">{name}</h2>
                  <p className="text-helper text-text-secondary">
                    {t('since', {
                      date: formatDate(customer.registeredAt, lang, { withWeekday: false, withYear: true }),
                    })}
                    {customer.isDisabled && ` · ${t('disabled')}`}
                  </p>
                </div>
              </div>
              <CustomerContact
                customerId={customer.id}
                masked={customer.phoneMasked ?? null}
                canReveal={me.permissions.includes('Admin.Customers.ViewContact')}
              />
            </Card>

            <section aria-label={t('stats')} className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <KpiTile
                label={t('kpi.bookings')}
                value={formatNumber(customer.bookings, lang)}
                icon="calendar"
              />
              <KpiTile
                label={t('kpi.completed')}
                value={formatNumber(customer.completed, lang)}
                icon="check"
              />
              <KpiTile label={t('kpi.cancelled')} value={formatNumber(customer.cancelled, lang)} icon="ban" />
              <KpiTile label={t('kpi.noShows')} value={formatNumber(customer.noShows, lang)} icon="alert" />
            </section>

            {canBookings && (
              <div className="grid gap-4 lg:grid-cols-2">
                <Card as="section" className="flex flex-col gap-3 p-5">
                  <div className="flex items-baseline justify-between gap-2">
                    <h2 className="text-h3 font-bold text-navy-900">{t('upcoming')}</h2>
                    <Link
                      href={`/admin/bookings?customer=${customer.id}`}
                      className="text-helper font-bold text-brand-700 hover:underline"
                    >
                      {t('allBookings')}
                    </Link>
                  </div>
                  {list(upcoming?.data?.items, t('noUpcoming'))}
                </Card>
                <Card as="section" className="flex flex-col gap-3 p-5">
                  <h2 className="text-h3 font-bold text-navy-900">{t('previous')}</h2>
                  {list(previous?.data?.items, t('noPrevious'))}
                </Card>
              </div>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
