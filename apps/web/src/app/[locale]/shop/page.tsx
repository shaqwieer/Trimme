import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopStatusBadge } from '@/components/admin/ShopStatusBadge';
import { BookingRow } from '@/components/shop/board/BookingRow';
import { LiveRefresh } from '@/components/shop/live/OperationsLive';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { BarChart } from '@/components/ui/charts';
import { KpiTile } from '@/components/ui/cards';
import { EmptyState, ErrorState, InlineAlert } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { type AppLocale, formatDate, formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { hourLabel, loadPercent, trimHours } from '@/lib/shop/board';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The operational overview (s-overview 2150–2227, spec §13): today's KPIs, what comes next, each professional's load and
 * the last week by hour. The shop comes from the session (GET /shop/me), never from the URL; live changes re-render it.
 */
export default async function ShopHomePage({ params }: PageProps<'/[locale]/shop'>) {
  const { locale: raw } = await params;
  const locale = asLocale(raw) as AppLocale;
  const [t, tHome] = await Promise.all([
    getTranslations({ locale, namespace: 'shopBoard.overview' }),
    getTranslations({ locale, namespace: 'shopHome' }),
  ]);

  return (
    <ShopFrame locale={locale} path="/shop" title={tHome('title')}>
      {async (me, shop) => {
        const identity = (
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-h2 font-bold text-navy-900" data-testid="shop-name">
              {shop.name}
            </h2>
            <span className="sr-only">{tHome('statusLabel')}</span>
            <ShopStatusBadge status={shop.status as 'Active' | 'Draft' | 'Suspended'} />
          </div>
        );
        if (shop.status === 'Suspended') {
          return (
            <section className="flex max-w-[720px] flex-col gap-4">
              {identity}
              <InlineAlert tone="danger" title={tHome('suspendedTitle')}>
                {tHome('suspendedBody')}
              </InlineAlert>
            </section>
          );
        }
        if (!me.permissions.includes('Shop.Bookings.Read')) {
          return (
            <section className="flex max-w-[720px] flex-col gap-4">
              {identity}
              <EmptyState icon="shield" title={t('noAccess')} />
            </section>
          );
        }

        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/shop/dashboard/overview');
        if (!data) return <ErrorState />;
        const { kpis } = data;
        const change =
          kpis.previousDayTotal > 0
            ? Math.round(((kpis.total - kpis.previousDayTotal) / kpis.previousDayTotal) * 100)
            : null;
        const hours = trimHours(data.hourly);

        return (
          <div className="flex max-w-[1280px] flex-col gap-5" data-testid="shop-overview">
            <LiveRefresh />
            <div className="flex flex-wrap items-baseline justify-between gap-3">
              {identity}
              <p className="text-helper text-text-secondary">
                {formatDate(`${data.date}T12:00:00Z`, locale, { withYear: true, timeZone: 'UTC' })}
              </p>
            </div>
            {shop.status === 'Draft' && (
              <InlineAlert tone="warning" title={tHome('draftTitle')}>
                {tHome('draftBody')}
              </InlineAlert>
            )}

            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5" data-testid="shop-kpis">
              <KpiTile
                label={t('kpis.total')}
                value={formatNumber(kpis.total, locale)}
                icon="calendar"
                delta={
                  change === null
                    ? undefined
                    : {
                        text: t('kpis.vsYesterday', {
                          percent: `${change > 0 ? '+' : ''}${formatNumber(change, locale)}`,
                        }),
                        tone: 'neutral',
                      }
                }
              />
              <KpiTile
                label={t('kpis.pending')}
                value={formatNumber(kpis.pendingUpcoming, locale)}
                icon="clock"
                delta={kpis.pendingUpcoming > 0 ? { text: t('kpis.needsAction'), tone: 'bad' } : undefined}
              />
              <KpiTile
                label={t('kpis.completed')}
                value={formatNumber(kpis.completed, locale)}
                icon="check"
                delta={{
                  text: t('kpis.outOf', { total: formatNumber(kpis.total, locale) }),
                  tone: 'neutral',
                }}
              />
              <KpiTile
                label={t('kpis.noShow')}
                value={formatNumber(kpis.noShow, locale)}
                icon="ban"
                delta={
                  kpis.total > 0
                    ? {
                        text: t('kpis.share', {
                          percent: formatNumber((kpis.noShow / kpis.total) * 100, locale, 1),
                        }),
                        tone: kpis.noShow > 0 ? 'bad' : 'neutral',
                      }
                    : undefined
                }
              />
              <KpiTile
                label={t('kpis.free')}
                value={t('kpis.freeValue', {
                  hours: Math.floor(kpis.freeMinutes / 60),
                  minutes: kpis.freeMinutes % 60,
                })}
                icon="users"
                delta={{ text: t('kpis.cancelled', { count: kpis.cancelled }), tone: 'neutral' }}
              />
            </div>

            <div className="grid items-start gap-5 xl:grid-cols-[1.5fr_1fr]">
              <section
                aria-labelledby="upcoming-title"
                className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1"
              >
                <div className="flex items-center justify-between gap-3">
                  <h3 id="upcoming-title" className="text-h3 font-bold text-navy-900">
                    {t('upcoming.title')}
                  </h3>
                  <Link href="/shop/calendar" className="text-label font-bold text-brand-700 hover:underline">
                    {t('upcoming.calendar')}
                  </Link>
                </div>
                {data.upcoming.length === 0 ? (
                  <p className="py-6 text-center text-label text-text-secondary">{t('upcoming.empty')}</p>
                ) : (
                  <ul className="flex flex-col gap-2" data-testid="upcoming-list">
                    {data.upcoming.map((booking, index) => (
                      <li key={booking.id}>
                        <BookingRow booking={booking} timeZone={data.timeZone} highlight={index === 0} />
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <section
                aria-labelledby="load-title"
                className="flex flex-col gap-4 rounded-card border border-border bg-surface p-5 shadow-e1"
              >
                <h3 id="load-title" className="text-h3 font-bold text-navy-900">
                  {t('load.title')}
                </h3>
                <ul className="flex flex-col gap-4" data-testid="professional-load">
                  {data.professionals.map((p) => {
                    const percent = loadPercent(p.bookedMinutes, p.availableMinutes);
                    const name = localizedName(locale, p.nameAr, p.nameEn);
                    return (
                      <li key={p.id} className="flex flex-col gap-1.5">
                        <div className="flex items-center justify-between gap-2 text-label">
                          <span className="font-bold text-text-primary">{name}</span>
                          <span className="text-helper text-text-secondary">
                            {p.onLeave
                              ? t('load.onLeave')
                              : !p.working
                                ? t('load.off')
                                : t('load.value', {
                                    bookings: p.bookings,
                                    percent: formatNumber(percent, locale),
                                  })}
                          </span>
                        </div>
                        <div
                          role="meter"
                          aria-label={name}
                          aria-valuemin={0}
                          aria-valuemax={100}
                          aria-valuenow={percent}
                          className="h-2 overflow-hidden rounded-full bg-bg-subtle"
                        >
                          <span
                            className={`block h-full rounded-full ${percent > 80 ? 'bg-navy-900' : 'bg-brand-500'}`}
                            style={{ width: `${p.working && !p.onLeave ? Math.max(percent, 2) : 0}%` }}
                          />
                        </div>
                      </li>
                    );
                  })}
                </ul>
              </section>
            </div>

            <section className="rounded-card border border-border bg-surface p-5 shadow-e1">
              <BarChart
                title={t('hourly.title')}
                subtitle={t('hourly.subtitle')}
                labelHeader={t('hourly.hour')}
                valueHeader={t('hourly.count')}
                data={hours.map((h) => ({ label: hourLabel(h.hour, locale), value: h.count }))}
              />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
