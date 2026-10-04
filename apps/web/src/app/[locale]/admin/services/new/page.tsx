import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminServiceEditor } from '@/components/admin/AdminCatalog';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * A new service for one shop, added by the admin from the shop's Services tab (D-127): the shop's own name, price and
 * duration, and the shop's barbers who do it.
 */
export default async function AdminNewServicePage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/services/new'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminServices.manage' });
  const tShops = await getTranslations({ locale: lang, namespace: 'adminShops' });
  const shopId = firstParam(query.shopId) ?? '';

  return (
    <AdminFrame
      locale={locale}
      path="/admin/services/new"
      title={t('newTitle')}
      permission="Admin.ShopServices.Manage"
    >
      {async (me) => {
        if (!shopId) return <EmptyState icon="store" title={t('noShop')} />;
        const api = await getServerApi();
        const [{ data: shop, response }, { data: categories }, { data: professionals }] = await Promise.all([
          api.GET('/api/v1/admin/shops/{shopId}', { params: { path: { shopId } } }),
          api.GET('/api/v1/public/service-categories'),
          me.permissions.includes('Admin.Professionals.View')
            ? api.GET('/api/v1/admin/professionals', { params: { query: { shopId, pageSize: 100 } } })
            : Promise.resolve({ data: undefined }),
        ]);
        if (response.status === 404 || response.status === 400)
          return <EmptyState icon="store" title={tShops('detail.notFoundTitle')} body={t('noShop')} />;
        if (!shop) return <ErrorState />;

        const shopName = lang === 'ar' ? shop.nameAr : shop.nameEn;
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb
              items={[
                { label: tShops('detail.back'), href: '/admin/shops' },
                { label: shopName, href: `/admin/shops/${shop.id}?tab=services` },
                { label: t('newTitle') },
              ]}
            />
            <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
              <AdminServiceEditor
                shopId={shop.id}
                categories={categories ?? []}
                professionals={(professionals?.items ?? [])
                  .filter((pro) => pro.status === 'Active')
                  .map((pro) => ({ id: pro.id, name: lang === 'ar' ? pro.nameAr : pro.nameEn }))}
              />
            </section>
          </div>
        );
      }}
    </AdminFrame>
  );
}
