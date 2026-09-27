import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { CatalogStatusBadge } from '@/components/catalog/CatalogStatusBadge';
import { PackageForm } from '@/components/catalog/PackageForm';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Edit one of the shop's own packages. */
export default async function ShopPackagePage({ params }: PageProps<'/[locale]/shop/packages/[packageId]'>) {
  const { locale, packageId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopServices' });
  const tCatalog = await getTranslations({ locale: lang, namespace: 'catalog' });

  return (
    <ShopFrame locale={locale} path={`/shop/packages/${packageId}`} title={t('form.editPackageTitle')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const [{ data: pkg }, { data: services }] = await Promise.all([
          api.GET('/api/v1/shop/packages/{packageId}', { params: { path: { packageId } } }),
          api.GET('/api/v1/shop/services'),
        ]);
        if (!pkg) return <EmptyState icon="tag" title={t('emptyPackages.title')} />;

        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb
              items={[
                { label: t('form.back'), href: '/shop/services?tab=packages' },
                { label: localizedName(lang, pkg.nameAr, pkg.nameEn) },
              ]}
            />
            <div className="flex items-center gap-3">
              <h2 className="text-h2 font-bold text-navy-900">
                {localizedName(lang, pkg.nameAr, pkg.nameEn)}
              </h2>
              <CatalogStatusBadge item={pkg} />
            </div>
            {pkg.moderation === 'Hidden' && pkg.moderationReason && (
              <InlineAlert tone="danger" title={tCatalog('hiddenReason', { reason: pkg.moderationReason })} />
            )}
            {!pkg.isArchived && !pkg.isBookable && pkg.moderation === 'Visible' && (
              <InlineAlert tone="warning" title={tCatalog('unbookable')} />
            )}
            <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
              {pkg.isArchived ? (
                <InlineAlert tone="info" title={tCatalog('status.archived')} />
              ) : (
                <PackageForm services={services ?? []} pkg={pkg} />
              )}
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
