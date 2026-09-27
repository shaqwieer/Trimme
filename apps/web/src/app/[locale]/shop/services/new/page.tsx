import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ServiceForm } from '@/components/catalog/ServiceForm';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** New service with the shop's own name, price and duration (DV-A01). */
export default async function NewShopServicePage({ params }: PageProps<'/[locale]/shop/services/new'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopServices.form' });

  return (
    <ShopFrame locale={locale} path="/shop/services/new" title={t('newTitle')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const { data: categories } = await api.GET('/api/v1/public/service-categories');
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('back'), href: '/shop/services' }, { label: t('newTitle') }]} />
            <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
              <ServiceForm categories={categories ?? []} />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
