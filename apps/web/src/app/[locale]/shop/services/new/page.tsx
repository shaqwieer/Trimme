import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopServiceEditor } from '@/components/catalog/ShopServices';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** A new service with the shop's own name, price and duration (DV-A01), and the shop's own barbers who do it (D-137). */
export default async function NewShopServicePage({ params }: PageProps<'/[locale]/shop/services/new'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopServices.form' });

  return (
    <ShopFrame locale={locale} path="/shop/services/new" title={t('newTitle')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const [{ data: categories }, { data: professionals }] = await Promise.all([
          api.GET('/api/v1/public/service-categories'),
          api.GET('/api/v1/shop/professionals'),
        ]);
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('back'), href: '/shop/services' }, { label: t('newTitle') }]} />
            <section className="rounded-card border border-border bg-surface p-4 shadow-e1 md:p-6">
              <ShopServiceEditor
                categories={categories ?? []}
                professionals={(professionals ?? [])
                  .filter((pro) => pro.status === 'Active')
                  .map((pro) => ({ id: pro.id, name: localizedName(lang, pro.nameAr, pro.nameEn) }))}
              />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
