import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { StatusBadge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { localDateParam, startOfLocalDay } from '@/lib/admin/admin';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatNumber, formatTime } from '@/lib/i18n/format';
import { addDays } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { chipOf, STATUS_CHIPS, statusesOf } from '@/lib/shop/board';
import { bookingSource } from '@/lib/booking/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;
const CHANNELS = ['Online', 'WalkIn'] as const;

/**
 * Bookings across every shop (a-appointments, DV-A09, R-AD-05): status chips with counts, a date range on the platform
 * calendar, channel, and search by customer name or booking reference only (never a phone). Filters are URL state, so
 * a view can be shared; the list is paged on the server and becomes cards on phones.
 */
export default async function AdminBookingsPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/bookings'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminBookings' });
  const tChips = await getTranslations({ locale: lang, namespace: 'shopBoard.appointments.chips' });
  const tChannel = await getTranslations({ locale: lang, namespace: 'shopBoard.drawer.channel' });

  const chip = chipOf(firstParam(query.status));
  const from = localDateParam(firstParam(query.from));
  const to = localDateParam(firstParam(query.to));
  const channelParam = firstParam(query.channel);
  const channel = (CHANNELS as readonly string[]).includes(channelParam ?? '')
    ? (channelParam as (typeof CHANNELS)[number])
    : undefined;
  const search = firstParam(query.q)?.trim() || undefined;
  const shopId = firstParam(query.shop) || undefined;
  const customerId = firstParam(query.customer) || undefined;
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  const keep = {
    status: chip === 'all' ? undefined : chip,
    from,
    to,
    channel,
    q: search,
    shop: shopId,
    customer: customerId,
  };
  const hrefWith = (changes: Record<string, string | number | undefined>) => {
    const next = new URLSearchParams();
    for (const [key, value] of Object.entries({ ...keep, ...changes })) {
      if (value !== undefined && value !== '') next.set(key, String(value));
    }
    const text = next.toString();
    return `/admin/bookings${text ? `?${text}` : ''}`;
  };

  return (
    <AdminFrame locale={locale} path="/admin/bookings" title={t('title')} permission="Admin.Bookings.View">
      {async () => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/bookings', {
          params: {
            query: {
              status: chip === 'all' ? undefined : statusesOf(chip),
              from: from ? startOfLocalDay(from) : undefined,
              to: to ? startOfLocalDay(addDays(to, 1)) : undefined,
              channel,
              search,
              shopId,
              customerId,
              page,
              pageSize: PAGE_SIZE,
            },
          },
        });
        if (!data) return <ErrorState />;
        const countOf = (value: (typeof STATUS_CHIPS)[number]) =>
          value === 'all' ? data.counts.all : data.counts[value === 'noShow' ? 'noShow' : value];

        return (
          <div className="flex flex-col gap-4">
            <form
              method="get"
              role="search"
              aria-label={t('filters')}
              className="grid gap-3 rounded-card border border-border bg-surface p-4 shadow-e1 md:grid-cols-2 lg:grid-cols-[2fr_1fr_1fr_1fr_auto]"
            >
              {chip !== 'all' && <input type="hidden" name="status" value={chip} />}
              {shopId && <input type="hidden" name="shop" value={shopId} />}
              {customerId && <input type="hidden" name="customer" value={customerId} />}
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('search')}
                <input
                  name="q"
                  type="search"
                  defaultValue={search}
                  placeholder={t('searchPlaceholder')}
                  className="min-h-11 min-w-0 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input font-normal text-text-primary placeholder:text-text-placeholder"
                />
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('from')}
                <input
                  name="from"
                  type="date"
                  defaultValue={from}
                  className="min-h-11 min-w-0 rounded-field border-[1.5px] border-border-input bg-surface px-3 font-latin text-input font-normal text-text-primary"
                />
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('to')}
                <input
                  name="to"
                  type="date"
                  defaultValue={to}
                  className="min-h-11 min-w-0 rounded-field border-[1.5px] border-border-input bg-surface px-3 font-latin text-input font-normal text-text-primary"
                />
              </label>
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('channel')}
                <select
                  name="channel"
                  defaultValue={channel ?? ''}
                  className="min-h-11 min-w-0 rounded-field border-[1.5px] border-border-input bg-surface px-3 text-input font-normal text-text-primary"
                >
                  <option value="">{t('allChannels')}</option>
                  {CHANNELS.map((value) => (
                    <option key={value} value={value}>
                      {tChannel(value)}
                    </option>
                  ))}
                </select>
              </label>
              <div className="flex items-end gap-2">
                <button
                  type="submit"
                  className="min-h-11 rounded-button bg-navy-900 px-4 text-label font-bold text-on-navy"
                >
                  {t('apply')}
                </button>
                {(search || from || to || channel || shopId || customerId) && (
                  <ButtonLink
                    href={chip === 'all' ? '/admin/bookings' : `/admin/bookings?status=${chip}`}
                    variant="ghost"
                    size="md"
                  >
                    {t('clear')}
                  </ButtonLink>
                )}
              </div>
            </form>

            {(shopId || customerId) && (
              <p className="text-caption text-text-secondary">
                {customerId ? t('scopedCustomer') : t('scopedShop')}
              </p>
            )}

            <nav aria-label={t('statusFilter')}>
              <ul className="flex flex-wrap gap-2">
                {STATUS_CHIPS.map((value) => (
                  <li key={value}>
                    <Link
                      href={hrefWith({ status: value === 'all' ? undefined : value, page: undefined })}
                      aria-current={value === chip ? 'page' : undefined}
                      className={`inline-flex min-h-11 items-center gap-1.5 rounded-pill border-[1.5px] px-3.5 text-label font-bold ${
                        value === chip
                          ? 'border-navy-900 bg-navy-900 text-on-navy'
                          : 'border-border-strong bg-surface text-text-strong'
                      }`}
                    >
                      {tChips(value)}
                      <span className="font-latin">{formatNumber(countOf(value), lang)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>

            <ResponsiveTable
              caption={t('caption')}
              rows={[...data.items]}
              rowKey={(b) => b.booking.id}
              empty={<EmptyState icon="calendar" title={t('empty.title')} body={t('empty.body')} />}
              columns={[
                {
                  key: 'when',
                  header: t('columns.when'),
                  mobile: 'primary',
                  width: '1.2fr',
                  cell: (b) => (
                    <Link
                      href={`/admin/bookings/${b.booking.id}`}
                      className="font-bold text-brand-700 hover:underline"
                    >
                      {formatTime(b.booking.startsAt, lang)}
                      <span className="block text-helper font-normal text-text-secondary">
                        {formatDate(b.booking.startsAt, lang, { withWeekday: false })}
                      </span>
                    </Link>
                  ),
                },
                {
                  key: 'customer',
                  header: t('columns.customer'),
                  width: '1.6fr',
                  cell: (b) => (
                    <span>
                      <span className="font-semibold text-text-primary">{b.booking.customerName}</span>
                      <span className="block text-helper text-text-secondary">
                        {localizedName(lang, b.booking.item.nameAr, b.booking.item.nameEn)}
                      </span>
                    </span>
                  ),
                },
                {
                  key: 'shop',
                  header: t('columns.shop'),
                  width: '1.6fr',
                  cell: (b) => (
                    <span>
                      <span className="font-semibold text-text-primary">
                        {localizedName(lang, b.shop.nameAr, b.shop.nameEn)}
                      </span>
                      <span className="block text-helper text-text-secondary">
                        {localizedName(lang, b.booking.professional.nameAr, b.booking.professional.nameEn)}
                      </span>
                    </span>
                  ),
                },
                {
                  key: 'status',
                  header: t('columns.status'),
                  cell: (b) => <StatusBadge kind="booking" status={b.booking.status} size="sm" />,
                },
                {
                  key: 'source',
                  header: t('columns.source'),
                  cell: (b) => tChannel(bookingSource(b.booking)),
                },
                {
                  key: 'reference',
                  header: t('columns.reference'),
                  cell: (b) => (
                    <span dir="ltr" className="font-latin text-helper">
                      {b.booking.reference}
                    </span>
                  ),
                },
              ]}
            />
            {data.total > data.pageSize && (
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                total={data.total}
                hrefForPage={(target) => hrefWith({ page: target })}
              />
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
