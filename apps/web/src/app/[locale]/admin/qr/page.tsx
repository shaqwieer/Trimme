import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { RankedBars } from '@/components/admin/ops/OverviewParts';
import { CreateQrCodeDialog, QrCodeToggle } from '@/components/qr/QrAdminActions';
import { QrDownloads, QrMaterials, QrStatusBadge, QrUrl, type QrCode } from '@/components/qr/QrParts';
import { Card, KpiTile } from '@/components/ui/cards';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatNumber } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { QR_PERIODS, qrPeriod } from '@/lib/qr';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const STATUSES = ['all', 'active', 'inactive'] as const;

/**
 * QR codes and their analytics (a-reviews QR card 3038–3052, c-qr print materials 2084–2114, DV-A14, R-AD-09, D-114): the
 * period's scans, credited bookings and conversion, scans per shop, and every code with its figures, files (PNG, SVG,
 * PDF, A5 poster) and on/off switch. Admins with `Admin.Qr.Manage` create codes; nothing is ever deleted.
 */
export default async function AdminQrPage({ params, searchParams }: PageProps<'/[locale]/admin/qr'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminQr' });
  const days = qrPeriod(firstParam(query.days));
  const statusParam = firstParam(query.status);
  const status = STATUSES.includes(statusParam as (typeof STATUSES)[number])
    ? (statusParam as (typeof STATUSES)[number])
    : 'all';
  const page = Math.max(1, Number(firstParam(query.page)) || 1);
  const href = (next: { days?: number; status?: string; page?: number }) => {
    const search = new URLSearchParams();
    const d = next.days ?? days;
    const s = next.status ?? status;
    if (d !== 30) search.set('days', String(d));
    if (s !== 'all') search.set('status', s);
    if (next.page && next.page > 1) search.set('page', String(next.page));
    const text = search.toString();
    return text ? `/admin/qr?${text}` : '/admin/qr';
  };

  return (
    <AdminFrame locale={locale} path="/admin/qr" title={t('title')} permission="Admin.Qr.View">
      {async (me) => {
        const api = await getServerApi();
        const canManage = me.permissions.includes('Admin.Qr.Manage');
        const [analytics, codes, shops] = await Promise.all([
          api.GET('/api/v1/admin/qr/analytics', { params: { query: { days } } }),
          api.GET('/api/v1/admin/qr/codes', {
            params: {
              query: {
                days,
                page,
                pageSize: 20,
                active: status === 'all' ? undefined : status === 'active',
              },
            },
          }),
          canManage
            ? api.GET('/api/v1/admin/shops', { params: { query: { page: 1, pageSize: 100 } } })
            : Promise.resolve(undefined),
        ]);
        if (!analytics.data || !codes.data) return <ErrorState />;
        const { totals, byShop } = analytics.data;
        const list = codes.data;
        const pct = (value: number) => `${formatNumber(value, lang, 1)}%`;
        const day = (date: string) =>
          formatDate(`${date}T12:00:00Z`, lang, { withYear: true, timeZone: 'UTC' });
        const targetName = (code: QrCode) =>
          code.targetType === 'Professional'
            ? t('target.Professional', {
                name: localizedName(lang, code.professionalNameAr ?? '', code.professionalNameEn),
              })
            : t('target.Shop');

        return (
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div className="flex flex-col gap-2">
                <p className="text-body text-text-secondary">
                  {t('subtitle', { from: day(analytics.data.from), to: day(analytics.data.to) })}
                </p>
                <LinkTabs
                  label={t('period.label')}
                  tabs={QR_PERIODS.map((value) => ({
                    href: href({ days: value, page: 1 }),
                    label: t(`period.d${value}`),
                    active: value === days,
                  }))}
                />
              </div>
              {canManage && shops?.data && (
                <CreateQrCodeDialog
                  shops={shops.data.items.map((s) => ({
                    id: s.id,
                    name: localizedName(lang, s.nameAr, s.nameEn),
                  }))}
                />
              )}
            </div>

            <section
              aria-label={t('title')}
              className="grid grid-cols-1 gap-3 md:grid-cols-3"
              data-testid="qr-kpis"
            >
              <KpiTile label={t('kpi.visits')} value={formatNumber(totals.visits, lang)} icon="qr" />
              <KpiTile
                label={t('kpi.bookings')}
                value={formatNumber(totals.bookings, lang)}
                icon="calendar"
              />
              <KpiTile
                label={t('kpi.conversion')}
                value={pct(totals.conversionRate)}
                icon="chart"
                delta={{
                  text: t('kpi.window', { days: formatNumber(analytics.data.attributionDays, lang) }),
                  tone: 'neutral',
                }}
              />
            </section>

            <div className="grid gap-4 lg:grid-cols-[1fr_1.6fr]">
              <Card as="section" className="flex flex-col gap-4 p-5" aria-labelledby="qr-by-shop">
                <h2 id="qr-by-shop" className="text-h3 font-bold text-navy-900">
                  {t('byShop.title')}
                </h2>
                {byShop.length === 0 ? (
                  <p className="text-caption text-text-secondary">{t('byShop.empty')}</p>
                ) : (
                  <>
                    <RankedBars
                      label={t('byShop.label')}
                      rows={byShop.map((s) => ({
                        key: s.shopId,
                        name: localizedName(lang, s.shopNameAr, s.shopNameEn),
                        value: s.visits,
                      }))}
                    />
                    <ul
                      className="flex flex-col gap-1 text-helper text-text-secondary"
                      data-testid="qr-by-shop"
                    >
                      {byShop.map((s) => (
                        <li key={s.shopId} className="flex flex-wrap justify-between gap-2">
                          <span>{localizedName(lang, s.shopNameAr, s.shopNameEn)}</span>
                          <span>
                            {t('figures', {
                              visits: formatNumber(s.visits, lang),
                              bookings: formatNumber(s.bookings, lang),
                            })}{' '}
                            · {pct(s.conversionRate)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </>
                )}
              </Card>
              <QrMaterials namespace="adminQr" />
            </div>

            <section aria-labelledby="qr-codes" className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 id="qr-codes" className="text-h3 font-bold text-navy-900">
                  {t('codes.title')}
                </h2>
                <LinkTabs
                  label={t('filter.label')}
                  tabs={STATUSES.map((value) => ({
                    href: href({ status: value, page: 1 }),
                    label: t(`filter.${value}`),
                    active: value === status,
                  }))}
                />
              </div>
              <ResponsiveTable
                caption={t('codes.caption')}
                rows={list.items}
                rowKey={(code) => code.id}
                empty={<EmptyState icon="qr" title={t('codes.empty')} />}
                columns={[
                  {
                    key: 'code',
                    header: t('codes.code'),
                    mobile: 'primary',
                    width: '2fr',
                    cell: (code) => (
                      <div className="flex min-w-0 flex-col gap-0.5" data-code={code.code}>
                        <span className="font-bold text-text-primary">{code.label ?? targetName(code)}</span>
                        {code.label && (
                          <span className="text-helper text-text-secondary">{targetName(code)}</span>
                        )}
                        <QrUrl url={code.url} nowrap />
                      </div>
                    ),
                  },
                  {
                    key: 'shop',
                    header: t('codes.shop'),
                    cell: (code) => localizedName(lang, code.shopNameAr, code.shopNameEn),
                  },
                  {
                    key: 'visits',
                    header: t('codes.visits'),
                    align: 'end',
                    cell: (code) => <span className="font-latin">{formatNumber(code.visits, lang)}</span>,
                  },
                  {
                    key: 'bookings',
                    header: t('codes.bookings'),
                    align: 'end',
                    cell: (code) => <span className="font-latin">{formatNumber(code.bookings, lang)}</span>,
                  },
                  {
                    key: 'status',
                    header: t('codes.status'),
                    cell: (code) => <QrStatusBadge namespace="adminQr" active={code.isActive} />,
                  },
                  {
                    key: 'files',
                    header: t('codes.files'),
                    width: '1.6fr',
                    cell: (code) =>
                      code.isActive ? (
                        <QrDownloads
                          namespace="adminQr"
                          scope="admin"
                          code={code}
                          posterHref={`/admin/qr/${code.id}/poster`}
                        />
                      ) : null,
                  },
                ]}
                actions={
                  canManage
                    ? (code) => (
                        <QrCodeToggle
                          codeId={code.id}
                          code={code.code}
                          version={code.version}
                          active={code.isActive}
                        />
                      )
                    : undefined
                }
              />
              {list.total > list.pageSize && (
                <Pagination
                  page={list.page}
                  pageSize={list.pageSize}
                  total={list.total}
                  hrefForPage={(p) => href({ page: p })}
                />
              )}
            </section>
          </div>
        );
      }}
    </AdminFrame>
  );
}
