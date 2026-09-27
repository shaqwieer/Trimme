import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { CatalogStatusBadge } from '@/components/catalog/CatalogStatusBadge';
import { ServiceForm } from '@/components/catalog/ServiceForm';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Edit one of the shop's own services. Another shop's id is simply not found (tenant filter). */
export default async function ShopServicePage({ params }: PageProps<'/[locale]/shop/services/[serviceId]'>) {
  const { locale, serviceId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopServices' });
  const tCatalog = await getTranslations({ locale: lang, namespace: 'catalog' });

  return (
    <ShopFrame locale={locale} path={`/shop/services/${serviceId}`} title={t('form.editTitle')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const [{ data: service }, { data: categories }] = await Promise.all([
          api.GET('/api/v1/shop/services/{serviceId}', { params: { path: { serviceId } } }),
          api.GET('/api/v1/public/service-categories'),
        ]);
        if (!service) return <EmptyState icon="tag" title={t('empty.title')} />;

        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb
              items={[
                { label: t('form.back'), href: '/shop/services' },
                { label: localizedName(lang, service.nameAr, service.nameEn) },
              ]}
            />
            <div className="flex items-center gap-3">
              <h2 className="text-h2 font-bold text-navy-900">
                {localizedName(lang, service.nameAr, service.nameEn)}
              </h2>
              <CatalogStatusBadge item={service} />
            </div>
            {service.moderation === 'Hidden' && service.moderationReason && (
              <InlineAlert
                tone="danger"
                title={tCatalog('hiddenReason', { reason: service.moderationReason })}
              />
            )}
            <section className="rounded-card border border-border bg-surface p-6 shadow-e1">
              {service.isArchived ? (
                <InlineAlert tone="info" title={tCatalog('status.archived')} />
              ) : (
                <ServiceForm categories={categories ?? []} service={service} />
              )}
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
