import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { BookingTrend, RankedBars } from '@/components/admin/ops/OverviewParts';
import { StatusBadge } from '@/components/ui/Badge';
import { Card, KpiTile } from '@/components/ui/cards';
import { ResponsiveTable } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { deltaTone, OVERVIEW_DAYS, overviewDays, percentChange, pointChange } from '@/lib/admin/admin';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatNumber, formatRating } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The platform overview (a-overview, R-AD-01, D-101): KPIs for today or the last 7 or 30 days with their change, the
 * 14-day booking trend, the most booked categories, the busiest shops and the subscriptions to follow up. Every figure
 * comes from the API; nothing is computed or invented here.
 */
export default async function AdminHomePage({ params, searchParams }: PageProps<'/[locale]/admin'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminOverview' });
  const days = overviewDays(firstParam(query.days));

  return (
    <AdminFrame locale={locale} path="/admin" title={t('title')} permission="Admin.Dashboard.View">
      {async (me) => {
        const api = await getServerApi();
        const canSubscriptions = me.permissions.includes('Admin.Subscriptions.View');
        const [{ data }, subscriptions] = await Promise.all([
          api.GET('/api/v1/admin/dashboard/overview', { params: { query: { days } } }),
          canSubscriptions
            ? api.GET('/api/v1/admin/subscriptions', {
                params: { query: { status: 'ExpiringSoon', pageSize: 5 } },
              })
            : Promise.resolve(undefined),
        ]);
        if (!data) return <ErrorState />;

        const pct = (value: number) => `${formatNumber(value, lang, 1)}%`;
        const change = (current: number, previous: number) => {
          const value = percentChange(current, previous);
          return value === null
            ? t('delta.noBase')
            : t('delta.percent', {
                value: formatNumber(Math.abs(value), lang),
                sign: value >= 0 ? '+' : '−',
              });
        };
        const points = (current: number, previous: number) => {
          const value = pointChange(current, previous);
          return t('delta.points', {
            value: formatNumber(Math.abs(value), lang, 1),
            sign: value >= 0 ? '+' : '−',
          });
        };
        const { period, previous } = data;
        const counts = subscriptions?.data?.counts;

        return (
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <p className="text-body text-text-secondary">
                {t('subtitle', {
                  date: formatDate(`${data.today}T12:00:00Z`, lang, { withYear: true, timeZone: 'UTC' }),
                })}
              </p>
              <LinkTabs
                label={t('period.label')}
                tabs={OVERVIEW_DAYS.map((value) => ({
                  href: value === 1 ? '/admin' : `/admin?days=${value}`,
                  label: t(`period.d${value}`),
                  active: value === days,
                }))}
              />
            </div>

            <section
              aria-label={t('kpis')}
              className="grid grid-cols-2 gap-3 lg:grid-cols-4"
              data-testid="admin-kpis"
            >
              <KpiTile
                label={t('kpi.appointmentsToday')}
                value={formatNumber(data.appointmentsToday, lang)}
                icon="calendar"
                delta={{
                  text: t('delta.vsYesterday', {
                    change: change(data.appointmentsToday, data.appointmentsYesterday),
                  }),
                  tone:
                    data.appointmentsYesterday === 0
                      ? 'neutral'
                      : deltaTone(data.appointmentsToday, data.appointmentsYesterday, true),
                }}
              />
              <KpiTile
                label={t('kpi.completionRate')}
                value={pct(period.completionRate)}
                icon="check"
                delta={{
                  text: t('delta.ofTotal', { count: formatNumber(period.total, lang) }),
                  tone: 'neutral',
                }}
              />
              <KpiTile
                label={t('kpi.cancellationRate')}
                value={pct(period.cancellationRate)}
                icon="ban"
                delta={{
                  text: points(period.cancellationRate, previous.cancellationRate),
                  tone: deltaTone(period.cancellationRate, previous.cancellationRate, false),
                }}
              />
              <KpiTile
                label={t('kpi.noShowRate')}
                value={pct(period.noShowRate)}
                icon="alert"
                delta={{
                  text: points(period.noShowRate, previous.noShowRate),
                  tone: deltaTone(period.noShowRate, previous.noShowRate, false),
                }}
              />
              <KpiTile
                label={t('kpi.activeShops')}
                value={formatNumber(data.shops.active, lang)}
                icon="store"
                delta={{
                  text: t('delta.ofShops', { total: formatNumber(data.shops.total, lang) }),
                  tone: 'neutral',
                }}
              />
              {counts && (
                <KpiTile
                  label={t('kpi.expiringSubscriptions')}
                  value={formatNumber(counts.expiringSoon, lang)}
                  icon="card"
                  delta={{
                    text: t('delta.endedOrSuspended', {
                      count: formatNumber(counts.expired + counts.suspended, lang),
                    }),
                    tone: counts.expired + counts.suspended > 0 ? 'bad' : 'neutral',
                  }}
                />
              )}
              <KpiTile
                label={t('kpi.activeProfessionals')}
                value={formatNumber(data.professionals.active, lang)}
                icon="users"
                delta={{
                  text: t('delta.added', { count: formatNumber(data.professionals.addedSince, lang) }),
                  tone: data.professionals.addedSince > 0 ? 'good' : 'neutral',
                }}
              />
              <KpiTile
                label={t('kpi.newCustomers')}
                value={formatNumber(data.newCustomers, lang)}
                icon="user"
                delta={{
                  text: change(data.newCustomers, data.newCustomersPrevious),
                  tone:
                    data.newCustomersPrevious === 0
                      ? 'neutral'
                      : deltaTone(data.newCustomers, data.newCustomersPrevious, true),
                }}
              />
            </section>

            <div className="grid gap-4 lg:grid-cols-[1.5fr_1fr]">
              <Card as="section" className="flex flex-col gap-4 p-5">
                <h2 className="text-h3 font-bold text-navy-900">{t('trend.title')}</h2>
                <BookingTrend days={data.trend} />
              </Card>
              <Card as="section" className="flex flex-col gap-4 p-5">
                <h2 className="text-h3 font-bold text-navy-900">{t('popular.title')}</h2>
                {data.popularCategories.length === 0 ? (
                  <p className="text-caption text-text-secondary">{t('popular.empty')}</p>
                ) : (
                  <RankedBars
                    label={t('popular.title')}
                    rows={data.popularCategories.map((c) => ({
                      key: `${c.kind}-${c.categoryId ?? ''}`,
                      name:
                        c.kind === 'Category'
                          ? localizedName(lang, c.nameAr ?? '', c.nameEn)
                          : t(`popular.${c.kind}`),
                      value: c.bookings,
                    }))}
                  />
                )}
              </Card>
            </div>

            <div className="grid gap-4 lg:grid-cols-[1.3fr_1fr]">
              <Card as="section" className="flex min-w-0 flex-col gap-4 p-5">
                <h2 className="text-h3 font-bold text-navy-900">{t('topShops.title')}</h2>
                <ResponsiveTable
                  caption={t('topShops.title')}
                  rows={[...data.topShops]}
                  rowKey={(s) => s.shopId}
                  empty={<EmptyState icon="store" title={t('topShops.empty')} />}
                  columns={[
                    {
                      key: 'shop',
                      header: t('topShops.shop'),
                      mobile: 'primary',
                      width: '2fr',
                      cell: (s) => (
                        <Link
                          href={`/admin/shops/${s.shopId}`}
                          className="font-bold text-brand-700 hover:underline"
                        >
                          {localizedName(lang, s.nameAr, s.nameEn)}
                        </Link>
                      ),
                    },
                    {
                      key: 'bookings',
                      header: t('topShops.bookings'),
                      cell: (s) => <span className="font-latin">{formatNumber(s.bookings, lang)}</span>,
                    },
                    {
                      key: 'cancellation',
                      header: t('topShops.cancellation'),
                      cell: (s) => (
                        <span
                          className={`font-latin ${s.cancellationRate > 10 ? 'font-bold text-danger-700' : ''}`}
                        >
                          {pct(s.cancellationRate)}
                        </span>
                      ),
                    },
                    {
                      key: 'rating',
                      header: t('topShops.rating'),
                      cell: (s) => (
                        <span className="font-latin">
                          {s.reviewCount > 0 ? `★ ${formatRating(s.rating, lang)}` : '—'}
                        </span>
                      ),
                    },
                  ]}
                />
              </Card>
              {subscriptions?.data && (
                <Card as="section" className="flex flex-col gap-4 p-5">
                  <div className="flex items-baseline justify-between gap-2">
                    <h2 className="text-h3 font-bold text-navy-900">{t('followUp.title')}</h2>
                    <Link
                      href="/admin/subscriptions?status=ExpiringSoon"
                      className="text-helper font-bold text-brand-700 hover:underline"
                    >
                      {t('followUp.all')}
                    </Link>
                  </div>
                  {subscriptions.data.items.length === 0 ? (
                    <p className="text-caption text-text-secondary">{t('followUp.empty')}</p>
                  ) : (
                    <ul className="flex flex-col divide-y divide-border-row">
                      {subscriptions.data.items.map((s) => (
                        <li key={s.shopId} className="flex items-center justify-between gap-3 py-2.5">
                          <div className="min-w-0">
                            <Link
                              href={`/admin/shops/${s.shopId}?tab=subscription`}
                              className="font-bold text-text-primary hover:underline"
                            >
                              {localizedName(lang, s.shopNameAr, s.shopNameEn)}
                            </Link>
                            <p className="text-helper text-text-secondary">
                              {t('followUp.ends', {
                                date: formatDate(`${s.endDate}T12:00:00Z`, lang, {
                                  withWeekday: false,
                                  timeZone: 'UTC',
                                }),
                              })}
                            </p>
                          </div>
                          <StatusBadge kind="subscription" status={s.status} size="sm" />
                        </li>
                      ))}
                    </ul>
                  )}
                  {counts && counts.expired + counts.suspended > 0 && (
                    <p className="text-helper text-text-secondary">
                      <Link
                        href="/admin/subscriptions?status=Expired"
                        className="font-bold text-brand-700 hover:underline"
                      >
                        {t('followUp.expired', { count: counts.expired })}
                      </Link>
                      {' · '}
                      <Link
                        href="/admin/subscriptions?status=Suspended"
                        className="font-bold text-brand-700 hover:underline"
                      >
                        {t('followUp.suspended', { count: counts.suspended })}
                      </Link>
                    </p>
                  )}
                </Card>
              )}
            </div>
          </div>
        );
      }}
    </AdminFrame>
  );
}
