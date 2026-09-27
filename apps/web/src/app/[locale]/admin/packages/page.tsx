import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ModerationControl } from '@/components/admin/AdminCatalog';
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

/** Shop packages across the platform (DV-S12, D-020): each shop's own price and duration; hide/show moderation. */
export default async function AdminPackagesPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/packages'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminServices' });
  const page = Math.max(1, Number(firstParam(query.page)) || 1);

  return (
    <AdminFrame
      locale={locale}
      path="/admin/packages"
      title={t('title')}
      permission="Admin.ShopServices.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/packages', {
          params: { query: { page, pageSize: 20 } },
        });
        const canModerate = me.permissions.includes('Admin.ShopServices.Moderate');
        if (!data) return <ErrorState />;

        return (
          <div className="flex flex-col gap-5">
            <AdminCatalogTabs active="packages" />
            {data.items.length === 0 ? (
              <EmptyState icon="layers" title={t('packages.empty.title')} body={t('packages.empty.body')} />
            ) : (
              <>
                <ResponsiveTable
                  caption={t('packages.caption')}
                  rows={data.items}
                  rowKey={(row) => row.id}
                  columns={[
                    {
                      key: 'name',
                      header: t('columns.service'),
                      cell: (row) => (
                        <span className="font-bold">{localizedName(lang, row.nameAr, row.nameEn)}</span>
                      ),
                    },
                    {
                      key: 'shop',
                      header: t('columns.shop'),
                      cell: (row) => (
                        <Link href={`/admin/shops/${row.shopId}`} className="hover:underline">
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
                    { key: 'items', header: t('columns.items'), cell: (row) => row.itemCount },
                    {
                      key: 'status',
                      header: t('columns.status'),
                      cell: (row) => (
                        <span className="flex flex-col gap-2">
                          <CatalogStatusBadge item={row} />
                          {canModerate && !row.isArchived && (
                            <ModerationControl
                              kind="package"
                              id={row.id}
                              hidden={row.moderation === 'Hidden'}
                              reason={row.moderationReason}
                            />
                          )}
                        </span>
                      ),
                    },
                  ]}
                />
                <Pagination
                  page={data.page}
                  pageSize={data.pageSize}
                  total={data.total}
                  hrefForPage={(target) => `/admin/packages?page=${target}`}
                />
              </>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
