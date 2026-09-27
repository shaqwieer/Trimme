import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminCatalogTabs } from '@/components/admin/AdminCatalogTabs';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { CatalogStatusBadge } from '@/components/catalog/CatalogStatusBadge';
import { Pagination, ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const PAGE_SIZE = 20;
const STATES = ['Active', 'Inactive', 'Archived', 'Hidden'] as const;

/**
 * Platform-wide view of shop-owned services (a-services, corrected per DV-S02): each row shows the shop's own price
 * and duration; there is no global price. Moderation and support override live on the detail page.
 */
export default async function AdminServicesPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/services'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminServices' });
  const search = firstParam(query.q)?.trim() || undefined;
  const state = STATES.find((value) => value === firstParam(query.state));
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame
      locale={locale}
      path="/admin/services"
      title={t('title')}
      permission="Admin.ShopServices.View"
    >
      {async () => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/services', {
          params: { query: { page, pageSize: PAGE_SIZE, search, state } },
        });
        const hrefFor = (target: number) =>
          `/admin/services?page=${target}${search ? `&q=${encodeURIComponent(search)}` : ''}${state ? `&state=${state}` : ''}`;

        return (
          <div className="flex flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            <AdminCatalogTabs active="services" />
            <form method="get" role="search" className="flex min-w-0 flex-wrap gap-2 md:max-w-[560px]">
              <label htmlFor="service-search" className="sr-only">
                {t('search')}
              </label>
              <input
                id="service-search"
                name="q"
                defaultValue={search}
                placeholder={t('search')}
                className="h-11 min-w-0 flex-1 rounded-field border border-border-input bg-surface px-3 text-body focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              />
              <label htmlFor="service-state" className="sr-only">
                {t('stateFilter')}
              </label>
              <select
                id="service-state"
                name="state"
                defaultValue={state ?? ''}
                className="h-11 rounded-field border border-border-input bg-surface px-3 text-body focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              >
                <option value="">{t('allStates')}</option>
                {STATES.map((value) => (
                  <option key={value} value={value}>
                    {t(`states.${value}`)}
                  </option>
                ))}
              </select>
              <button
                type="submit"
                className="h-11 rounded-button bg-navy-900 px-4 text-button font-bold text-on-navy focus-visible:shadow-[var(--focus-ring)] focus-visible:outline-none"
              >
                {t('searchSubmit')}
              </button>
            </form>

            {!data ? (
              <ErrorState />
            ) : data.items.length === 0 ? (
              <EmptyState icon="tag" title={t('empty.title')} body={t('empty.body')} />
            ) : (
              <>
                <ResponsiveTable
                  caption={t('caption')}
                  rows={data.items}
                  rowKey={(row) => row.id}
                  columns={[
                    {
                      key: 'service',
                      header: t('columns.service'),
                      cell: (row) => (
                        <Link
                          href={`/admin/services/${row.id}`}
                          className="font-bold text-text-link hover:underline"
                        >
                          {localizedName(lang, row.nameAr, row.nameEn)}
                        </Link>
                      ),
                    },
                    {
                      key: 'shop',
                      header: t('columns.shop'),
                      cell: (row) => (
                        <Link
                          href={`/admin/shops/${row.shopId}`}
                          className="text-text-primary hover:underline"
                        >
                          {lang === 'ar' ? row.shopNameAr : row.shopNameEn}
                        </Link>
                      ),
                    },
                    {
                      key: 'price',
                      header: t('columns.price'),
                      cell: (row) => formatPrice(row.price, lang, row.currency),
                    },
                    {
                      key: 'duration',
                      header: t('columns.duration'),
                      cell: (row) => formatDurationMinutes(row.durationMinutes, lang),
                    },
                    {
                      key: 'status',
                      header: t('columns.status'),
                      cell: (row) => <CatalogStatusBadge item={row} />,
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
