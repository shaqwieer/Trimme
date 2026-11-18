import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { KpiTile } from '@/components/ui/cards';
import { EmptyState, ErrorState, InlineAlert, PermissionDenied } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import type { components } from '@/lib/api/schema';
import { firstParam } from '@/lib/auth/paths';
import { type AppLocale, formatNumber, formatPrice, formatTime } from '@/lib/i18n/format';
import { formatLocalDate, todayLocal } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { asReportPeriod, REPORT_PERIODS, reportRange } from '@/lib/shop/reportPeriod';
import { cn } from '@/lib/cn';

export const metadata: Metadata = { robots: { index: false, follow: false } };

type Counts = components['schemas']['ReportCountsResponse'];

/**
 * The owner's reports and expected income (D-147, «٢-٤»): by day, week or month, for the shop and each barber — the
 * bookings and what became of them, moves to and from each barber, postponements, late cancellations, no-shows,
 * breaks, the average actual length (a visit under five minutes is highlighted: perhaps finished without a service)
 * and the expected income (no online payment: completed visits at their booked price, a free loyalty service at zero);
 * with loyalty on, the circles stamped and the rewards used, by who closed the visit. Then each booking with its actual
 * start and end. Tables and plain numbers, no chart library (the bundle budget). Barbers see none of it.
 */
export default async function ShopReportsPage({ params, searchParams }: PageProps<'/[locale]/shop/reports'>) {
  const [{ locale: raw }, query] = await Promise.all([params, searchParams]);
  const locale = asLocale(raw) as AppLocale;
  const t = await getTranslations({ locale, namespace: 'shopReports' });
  const tStatus = await getTranslations({ locale, namespace: 'status.booking' });

  return (
    <ShopFrame locale={locale} path="/shop/reports" title={t('title')}>
      {async (me, shop) => {
        if (!me.permissions.includes('Shop.Insights.View')) return <PermissionDenied homeHref="/shop" />;
        const period = asReportPeriod(firstParam(query.period));
        const asked = firstParam(query.date);
        const today = todayLocal(shop.timeZone);
        const date = asked && /^\d{4}-\d{2}-\d{2}$/.test(asked) ? asked : today;
        const range = reportRange(period, date);
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/shop/reports', {
          params: { query: { from: range.from, to: range.to } },
        });
        if (!data) return <ErrorState />;

        const money = (value: number) => formatPrice(value, locale, data.currency);
        const minutes = (value: number | null | undefined) =>
          value == null ? '—' : t('minutes', { count: formatNumber(value, locale) });
        const href = (next: { period?: string; date?: string }) =>
          `/shop/reports?period=${next.period ?? period}&date=${next.date ?? date}`;
        const label =
          range.from === range.to
            ? formatLocalDate(range.from, locale, { weekday: 'long', day: 'numeric', month: 'long' })
            : t('range', {
                from: formatLocalDate(range.from, locale, { day: 'numeric', month: 'long' }),
                to: formatLocalDate(range.to, locale, { day: 'numeric', month: 'long' }),
              });
        const columns: Array<[string, (c: Counts) => string]> = [
          [t('cols.bookings'), (c) => formatNumber(c.bookings, locale)],
          [t('cols.completed'), (c) => formatNumber(c.completed, locale)],
          [t('cols.income'), (c) => money(c.expectedIncome)],
          [t('cols.average'), (c) => minutes(c.averageMinutes)],
          [t('cols.short'), (c) => formatNumber(c.shortVisits, locale)],
          [t('cols.movedOut'), (c) => formatNumber(c.movedOut, locale)],
          [t('cols.movedIn'), (c) => formatNumber(c.movedIn, locale)],
          [t('cols.postponed'), (c) => formatNumber(c.postponed, locale)],
          [t('cols.lateCancellations'), (c) => formatNumber(c.lateCancellations, locale)],
          [t('cols.noShows'), (c) => formatNumber(c.noShows, locale)],
          [t('cols.closedLate'), (c) => formatNumber(c.closedLate, locale)],
          [t('cols.unclosed'), (c) => formatNumber(c.unclosed, locale)],
          [
            t('cols.breaks'),
            (c) =>
              t('breaksValue', {
                count: formatNumber(c.breaks, locale),
                minutes: formatNumber(c.breakMinutes, locale),
              }),
          ],
        ];
        const cell = 'px-3 py-2 text-start whitespace-nowrap';

        return (
          <div className="flex max-w-[1280px] flex-col gap-5" data-testid="shop-reports">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <nav aria-label={t('periodLabel')} className="flex gap-1 rounded-button bg-bg-subtle p-1">
                {REPORT_PERIODS.map((p) => (
                  <Link
                    key={p}
                    href={href({ period: p })}
                    aria-current={p === period ? 'page' : undefined}
                    className={cn(
                      'min-h-10 rounded-button px-4 py-2 text-label font-bold',
                      p === period ? 'bg-navy-900 text-on-navy' : 'text-text-strong hover:bg-surface',
                    )}
                  >
                    {t(`periods.${p}`)}
                  </Link>
                ))}
              </nav>
              <div className="flex items-center gap-2">
                <Link
                  href={href({ date: range.previous })}
                  className="min-h-10 rounded-button border border-border-input px-3 py-2 text-label font-bold text-text-strong hover:bg-bg-subtle"
                >
                  {t('previous')}
                </Link>
                <span className="text-label font-bold text-text-primary" data-testid="report-range">
                  {label}
                </span>
                <Link
                  href={href({ date: range.next })}
                  className="min-h-10 rounded-button border border-border-input px-3 py-2 text-label font-bold text-text-strong hover:bg-bg-subtle"
                >
                  {t('next')}
                </Link>
              </div>
            </div>

            <InlineAlert tone="info" title={t('expectedNote')} />

            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6" data-testid="report-kpis">
              <KpiTile label={t('kpis.income')} value={money(data.shop.expectedIncome)} icon="tag" />
              <KpiTile
                label={t('kpis.completed')}
                value={formatNumber(data.shop.completed, locale)}
                icon="check"
              />
              <KpiTile label={t('kpis.average')} value={minutes(data.shop.averageMinutes)} icon="clock" />
              <KpiTile label={t('kpis.noShows')} value={formatNumber(data.shop.noShows, locale)} icon="ban" />
              <KpiTile
                label={t('kpis.lateCancellations')}
                value={formatNumber(data.shop.lateCancellations, locale)}
                icon="x"
              />
              <KpiTile
                label={t('kpis.short')}
                value={formatNumber(data.shop.shortVisits, locale)}
                icon="alert"
              />
            </div>

            <section
              aria-labelledby="barbers-title"
              className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
            >
              <h2 id="barbers-title" className="text-h3 font-bold text-navy-900">
                {t('barbersTitle')}
              </h2>
              <div className="overflow-x-auto" tabIndex={0} role="region" aria-labelledby="barbers-title">
                <table className="min-w-full text-label" data-testid="report-barbers">
                  <thead className="border-b border-border text-helper text-text-secondary">
                    <tr>
                      <th scope="col" className={cell}>
                        {t('cols.barber')}
                      </th>
                      {columns.map(([title]) => (
                        <th key={title} scope="col" className={cell}>
                          {title}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border-row">
                    {data.barbers.map((b) => (
                      <tr key={b.professionalId}>
                        <th scope="row" className={cn(cell, 'font-bold text-text-primary')}>
                          {localizedName(locale, b.nameAr, b.nameEn)}
                        </th>
                        {columns.map(([title, value]) => (
                          <td key={title} className={cn(cell, 'text-text-strong')}>
                            {value(b.counts)}
                          </td>
                        ))}
                      </tr>
                    ))}
                    <tr className="bg-bg-subtle font-bold">
                      <th scope="row" className={cn(cell, 'text-navy-900')}>
                        {t('wholeShop')}
                      </th>
                      {columns.map(([title, value]) => (
                        <td key={title} className={cn(cell, 'text-navy-900')}>
                          {value(data.shop)}
                        </td>
                      ))}
                    </tr>
                  </tbody>
                </table>
              </div>
            </section>

            {data.loyalty && (
              <section
                aria-labelledby="loyalty-title"
                className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
                data-testid="report-loyalty"
              >
                <h2 id="loyalty-title" className="text-h3 font-bold text-navy-900">
                  {t('loyalty.title')}
                </h2>
                <p className="text-label text-text-strong">
                  {t('loyalty.summary', {
                    stamps: formatNumber(data.loyalty.stamps, locale),
                    redemptions: formatNumber(data.loyalty.redemptions, locale),
                  })}
                </p>
                {data.loyalty.byActor.length > 0 && (
                  <ul className="flex flex-col divide-y divide-border-row">
                    {data.loyalty.byActor.map((a) => (
                      <li key={a.name} className="flex items-center justify-between gap-3 py-2 text-label">
                        <span className="font-bold text-text-primary">{a.name}</span>
                        <span className="text-text-secondary">
                          {t('loyalty.byActor', {
                            stamps: formatNumber(a.stamps, locale),
                            redemptions: formatNumber(a.redemptions, locale),
                          })}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            )}

            <section
              aria-labelledby="visits-title"
              className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
            >
              <h2 id="visits-title" className="text-h3 font-bold text-navy-900">
                {t('visitsTitle')}
              </h2>
              {data.visitsTruncated && (
                <p className="text-helper text-text-secondary">
                  {t('truncated', { count: formatNumber(data.visits.length, locale) })}
                </p>
              )}
              {data.visits.length === 0 ? (
                <EmptyState icon="calendar" title={t('noVisits')} />
              ) : (
                <div className="overflow-x-auto" tabIndex={0} role="region" aria-labelledby="visits-title">
                  <table className="min-w-full text-label" data-testid="report-visits">
                    <thead className="border-b border-border text-helper text-text-secondary">
                      <tr>
                        {[
                          t('visit.when'),
                          t('visit.customer'),
                          t('cols.barber'),
                          t('visit.service'),
                          t('visit.status'),
                          t('visit.start'),
                          t('visit.end'),
                          t('visit.length'),
                          t('cols.income'),
                          t('visit.notes'),
                        ].map((title) => (
                          <th key={title} scope="col" className={cell}>
                            {title}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border-row">
                      {data.visits.map((v) => (
                        <tr key={v.id} className={v.underFiveMinutes ? 'bg-danger-50' : undefined}>
                          <td className={cell}>
                            {range.from === range.to
                              ? formatTime(v.startsAt, locale, data.timeZone)
                              : `${formatLocalDate(todayLocal(data.timeZone, new Date(v.startsAt)), locale, { day: 'numeric', month: 'short' })} ${formatTime(v.startsAt, locale, data.timeZone)}`}
                          </td>
                          <td className={cn(cell, 'font-bold text-text-primary')}>{v.customerName}</td>
                          <td className={cell}>
                            {localizedName(locale, v.professionalNameAr, v.professionalNameEn)}
                          </td>
                          <td className={cell}>
                            {localizedName(locale, v.itemNameAr, v.itemNameEn ?? null)}
                          </td>
                          <td className={cell}>
                            <StatusBadge kind="booking" status={v.status} size="sm" />
                          </td>
                          <td className={cell}>
                            {v.actualStartAt ? formatTime(v.actualStartAt, locale, data.timeZone) : '—'}
                          </td>
                          <td className={cell}>
                            {v.actualEndAt ? formatTime(v.actualEndAt, locale, data.timeZone) : '—'}
                          </td>
                          <td className={cell}>
                            {v.underFiveMinutes ? (
                              <Badge tone="danger" size="sm">
                                {minutes(v.actualMinutes)}
                              </Badge>
                            ) : (
                              minutes(v.actualMinutes)
                            )}
                          </td>
                          <td className={cell}>{v.status === 'Completed' ? money(v.expectedIncome) : '—'}</td>
                          <td className={cn(cell, 'text-helper text-text-secondary')}>
                            {[
                              v.underFiveMinutes && t('flags.short'),
                              v.closedLate && t('flags.closedLate', { name: v.closedBy ?? '—' }),
                              v.cancelledLate && t('flags.cancelledLate'),
                              v.unclosed && t('flags.unclosed'),
                              v.loyaltyReward && t('flags.loyalty'),
                              v.status !== 'Completed' && !v.unclosed && tStatus(v.status),
                            ]
                              .filter(Boolean)
                              .join(' · ')}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
