import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { PackageForm } from '@/components/catalog/PackageForm';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** New package of the shop's own services (D-020). */
export default async function NewShopPackagePage({ params }: PageProps<'/[locale]/shop/packages/new'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopServices.form' });

  return (
    <ShopFrame locale={locale} path="/shop/packages/new" title={t('newPackageTitle')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const { data: services } = await api.GET('/api/v1/shop/services');
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb
              items={[
                { label: t('back'), href: '/shop/services?tab=packages' },
                { label: t('newPackageTitle') },
              ]}
            />
            <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
              <PackageForm services={services ?? []} />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
