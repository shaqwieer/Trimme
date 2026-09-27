import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { CategoryEditor } from '@/components/admin/AdminCatalog';
import { AdminCatalogTabs } from '@/components/admin/AdminCatalogTabs';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { Badge } from '@/components/ui/Badge';
import { ResponsiveTable } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Platform service categories (a-services category column, DV-S02): names in both languages, icon, order, on/off. */
export default async function AdminCategoriesPage({
  params,
}: PageProps<'/[locale]/admin/services/categories'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminServices' });
  const tStatus = await getTranslations({ locale: lang, namespace: 'catalog.status' });

  return (
    <AdminFrame
      locale={locale}
      path="/admin/services/categories"
      title={t('title')}
      permission="Admin.ShopServices.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data } = await api.GET('/api/v1/admin/service-categories');
        const canManage = me.permissions.includes('Admin.ServiceCategories.Manage');
        if (!data) return <ErrorState />;

        return (
          <div className="flex flex-col gap-5">
            <AdminCatalogTabs active="categories" />
            {canManage && <CategoryEditor />}
            {data.length === 0 ? (
              <EmptyState icon="tag" title={t('categories.empty')} />
            ) : (
              <ResponsiveTable
                caption={t('categories.caption')}
                rows={data}
                rowKey={(row) => row.id}
                columns={[
                  {
                    key: 'name',
                    header: t('categories.nameAr'),
                    cell: (row) => (
                      <span className="flex items-center gap-2">
                        <Icon name={row.icon as 'tag'} className="size-4 text-brand-700" />
                        <span className="font-bold">{lang === 'ar' ? row.nameAr : row.nameEn}</span>
                      </span>
                    ),
                  },
                  { key: 'order', header: t('categories.order'), cell: (row) => row.displayOrder },
                  { key: 'services', header: t('categories.services'), cell: (row) => row.serviceCount },
                  {
                    key: 'status',
                    header: t('columns.status'),
                    cell: (row) => (
                      <Badge tone={row.isActive ? 'success' : 'neutral'}>
                        {row.isActive ? tStatus('active') : tStatus('inactive')}
                      </Badge>
                    ),
                  },
                  ...(canManage
                    ? [
                        {
                          key: 'actions',
                          header: t('categories.edit'),
                          cell: (row: (typeof data)[number]) => <CategoryEditor category={row} />,
                        },
                      ]
                    : []),
                ]}
              />
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
