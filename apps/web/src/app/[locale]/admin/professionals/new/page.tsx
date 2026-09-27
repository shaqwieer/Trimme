import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { CreateProfessionalForm } from '@/components/admin/ProfessionalForms';
import { Breadcrumb } from '@/components/ui/data';
import { ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Admin adds a professional to exactly one shop (DV-A07). `?shopId=` preselects the shop from the shop page. */
export default async function NewProfessionalPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/professionals/new'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminProfessionals' });
  const defaultShopId = firstParam(query.shopId);

  return (
    <AdminFrame
      locale={locale}
      path="/admin/professionals/new"
      title={t('create.title')}
      permission="Admin.Professionals.Create"
    >
      {async () => {
        const api = await getServerApi();
        // The shop list is bounded (the API caps a page at 100); a searchable picker replaces it when shops outgrow it.
        const { data } = await api.GET('/api/v1/admin/shops', {
          params: { query: { page: 1, pageSize: 100 } },
        });
        if (!data) return <ErrorState />;
        const shops = data.items.map((shop) => ({
          id: shop.id,
          name: lang === 'ar' ? shop.nameAr : shop.nameEn,
        }));

        return (
          <div className="flex flex-col gap-5">
            <Breadcrumb
              items={[
                { label: t('detail.back'), href: '/admin/professionals' },
                { label: t('create.title') },
              ]}
            />
            <CreateProfessionalForm
              shops={shops}
              defaultShopId={shops.some((s) => s.id === defaultShopId) ? defaultShopId : undefined}
            />
          </div>
        );
      }}
    </AdminFrame>
  );
}
