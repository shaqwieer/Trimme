import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminPackageEditor } from '@/components/admin/AdminCatalog';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** One shop package, edited by the admin who manages that shop's catalogue (D-130). */
export default async function AdminPackagePage({
  params,
}: PageProps<'/[locale]/admin/packages/[packageId]'>) {
  const { locale, packageId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminServices.manage' });
  const tShops = await getTranslations({ locale: lang, namespace: 'adminShops' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/packages/${packageId}`}
      title={t('editPackageTitle')}
      permission="Admin.ShopServices.Manage"
    >
      {async () => {
        const api = await getServerApi();
        const { data, response } = await api.GET('/api/v1/admin/packages/{packageId}', {
          params: { path: { packageId } },
        });
        if (response.status === 404 || response.status === 400)
          return <EmptyState icon="tag" title={t('packageNotFound')} />;
        if (!data) return <ErrorState />;
        const [{ data: shop }, { data: services }] = await Promise.all([
          api.GET('/api/v1/admin/shops/{shopId}', { params: { path: { shopId: data.shopId } } }),
          api.GET('/api/v1/admin/services', { params: { query: { shopId: data.shopId, pageSize: 100 } } }),
        ]);
        const shopName = shop ? (lang === 'ar' ? shop.nameAr : shop.nameEn) : '';
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb
              items={[
                { label: tShops('detail.back'), href: '/admin/shops' },
                { label: shopName, href: `/admin/shops/${data.shopId}?tab=services` },
                { label: localizedName(lang, data.package.nameAr, data.package.nameEn) },
              ]}
            />
            <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
              <AdminPackageEditor shopId={data.shopId} pkg={data.package} services={services?.items ?? []} />
            </section>
          </div>
        );
      }}
    </AdminFrame>
  );
}
