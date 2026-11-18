import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopPackageEditor } from '@/components/catalog/ShopPackages';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { ErrorState, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** A new package of the shop's own services, by the owner (D-147). */
export default async function ShopNewPackagePage({ params }: PageProps<'/[locale]/shop/packages/new'>) {
  const { locale } = await params;
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopServices' });

  return (
    <ShopFrame locale={locale} path="/shop/packages/new" title={t('packages.newTitle')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const { data: services } = await api.GET('/api/v1/shop/services');
        if (!services) return <ErrorState />;
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb
              items={[{ label: t('form.back'), href: '/shop/services' }, { label: t('packages.newTitle') }]}
            />
            <section className="rounded-card border border-border bg-surface p-4 shadow-e1 md:p-6">
              <ShopPackageEditor services={services} />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
