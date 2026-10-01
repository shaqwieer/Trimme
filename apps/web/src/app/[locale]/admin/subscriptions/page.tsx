import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { longDate } from '@/components/subscriptions/periods';
import { StatusBadge, SUBSCRIPTION_STATUSES, type SubscriptionStatus } from '@/components/ui/Badge';
import { KpiTile } from '@/components/ui/cards';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatNumber, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;
const FILTERS = SUBSCRIPTION_STATUSES.filter((s) => s !== 'None');

/**
 * a-subs (DV-A11): counts per status over every shop, then subscriptions by status (most urgent end date first). The
 * row action opens the shop's subscription tab, where activation, renewal, suspension and overrides are recorded.
 */
export default async function AdminSubscriptionsPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/subscriptions'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminSubscriptions' });
  const search = firstParam(query.q)?.trim() || undefined;
  const status = FILTERS.find((value) => value === firstParam(query.status));
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame
      locale={locale}
      path="/admin/subscriptions"
      title={t('title')}
      permission="Admin.Subscriptions.View"
    >
      {async () => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/subscriptions', {
          params: { query: { page, pageSize: PAGE_SIZE, status, search } },
        });
        if (!data) return <ErrorState />;
        const hrefFor = (target: number) =>
          `/admin/subscriptions?page=${target}${search ? `&q=${encodeURIComponent(search)}` : ''}${status ? `&status=${status}` : ''}`;
        const kpis: SubscriptionStatus[] = ['Active', 'ExpiringSoon', 'Expired', 'Suspended', 'None'];
        const countOf = (s: SubscriptionStatus) =>
          ({
            Active: data.counts.active,
            ExpiringSoon: data.counts.expiringSoon,
            Expired: data.counts.expired,
            Suspended: data.counts.suspended,
            None: data.counts.none,
          })[s];

        return (
          <div className="flex flex-col gap-5">
            <p className="text-body text-text-secondary">
              {t('intro', { days: data.expiringSoonThresholdDays })}
            </p>
            <div className="grid grid-cols-2 gap-3 md:grid-cols-5" data-testid="subscription-kpis">
              {kpis.map((s) => (
                <KpiTile key={s} label={t(`kpis.${s}`)} value={formatNumber(countOf(s), lang, 0)} />
              ))}
            </div>
            <form method="get" role="search" className="flex min-w-0 flex-wrap gap-2 md:max-w-[560px]">
              <label htmlFor="subscription-search" className="sr-only">
                {t('search')}
              </label>
              <input
                id="subscription-search"
                name="q"
                defaultValue={search}
                placeholder={t('search')}
                className="h-11 min-w-0 flex-1 rounded-field border border-border-input bg-surface px-3 text-body focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              />
              <label htmlFor="subscription-status" className="sr-only">
                {t('statusFilter')}
              </label>
              <select
                id="subscription-status"
                name="status"
                defaultValue={status ?? ''}
                className="h-11 rounded-field border border-border-input bg-surface px-3 text-body focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              >
                <option value="">{t('allStatuses')}</option>
                {FILTERS.map((value) => (
                  <option key={value} value={value}>
                    {t(`kpis.${value}`)}
                  </option>
                ))}
              </select>
              <button
                type="submit"
                className="min-h-11 rounded-button border-[1.5px] border-border-strong bg-surface px-4 text-label font-bold text-text-strong hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              >
                {t('searchSubmit')}
              </button>
            </form>

            {data.items.length === 0 ? (
              <EmptyState icon="calendar" title={t('empty.title')} body={t('empty.body')} />
            ) : (
              <>
                <ResponsiveTable
                  caption={t('caption')}
                  rows={data.items}
                  rowKey={(row) => row.shopId}
                  columns={[
                    {
                      key: 'shop',
                      header: t('columns.shop'),
                      mobile: 'primary',
                      cell: (row) => (
                        <span className="font-bold text-text-primary">
                          {lang === 'ar' ? row.shopNameAr : row.shopNameEn}
                        </span>
                      ),
                    },
                    {
                      key: 'plan',
                      header: t('columns.plan'),
                      cell: (row) => localizedName(lang, row.planNameAr, row.planNameEn),
                    },
                    { key: 'ends', header: t('columns.ends'), cell: (row) => longDate(row.endDate, lang) },
                    {
                      key: 'remaining',
                      header: t('columns.remaining'),
                      cell: (row) => formatNumber(row.daysRemaining, lang, 0),
                    },
                    {
                      key: 'status',
                      header: t('columns.status'),
                      cell: (row) => <StatusBadge kind="subscription" status={row.status} />,
                    },
                    {
                      key: 'amount',
                      header: t('columns.amount'),
                      cell: (row) => formatPrice(row.currentAmount, lang, row.currency),
                    },
                    {
                      key: 'action',
                      header: t('columns.action'),
                      cell: (row) => (
                        <Link
                          href={`/admin/shops/${row.shopId}?tab=subscription`}
                          className="font-bold text-text-link hover:underline"
                          aria-label={t('manageLabel', {
                            name: lang === 'ar' ? row.shopNameAr : row.shopNameEn,
                          })}
                        >
                          {t('manage')}
                        </Link>
                      ),
                    },
                  ]}
                />
                <Pagination
                  page={data.page}
                  pageSize={data.pageSize}
                  total={data.total}
                  hrefForPage={hrefFor}
                />
              </>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
