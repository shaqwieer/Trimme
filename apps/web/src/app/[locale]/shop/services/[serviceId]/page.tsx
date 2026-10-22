import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { CatalogStatusBadge } from '@/components/catalog/CatalogStatusBadge';
import { ShopServiceEditor } from '@/components/catalog/ShopServices';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, InlineAlert, PermissionDenied } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Edit one of the shop's own services and who does it (D-137). Another shop's id is simply not found (tenant filter). */
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
        const [{ data: service }, { data: categories }, { data: professionals }] = await Promise.all([
          api.GET('/api/v1/shop/services/{serviceId}', { params: { path: { serviceId } } }),
          api.GET('/api/v1/public/service-categories'),
          api.GET('/api/v1/shop/professionals'),
        ]);
        if (!service) return <EmptyState icon="tag" title={t('empty.title')} />;
        const name = localizedName(lang, service.nameAr, service.nameEn);

        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('form.back'), href: '/shop/services' }, { label: name }]} />
            <div className="flex flex-wrap items-center gap-3">
              <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
              <CatalogStatusBadge item={service} />
            </div>
            {service.moderation === 'Hidden' && service.moderationReason && (
              <InlineAlert
                tone="danger"
                title={tCatalog('hiddenReason', { reason: service.moderationReason })}
              />
            )}
            <section className="rounded-card border border-border bg-surface p-4 shadow-e1 md:p-6">
              {service.isArchived ? (
                <InlineAlert tone="info" title={tCatalog('status.archived')} />
              ) : (
                <ShopServiceEditor
                  service={service}
                  categories={categories ?? []}
                  // A disabled barber who still does it stays listed, so a save does not take them off unnoticed.
                  professionals={(professionals ?? [])
                    .filter((pro) => pro.status === 'Active' || service.professionalIds.includes(pro.id))
                    .map((pro) => ({ id: pro.id, name: localizedName(lang, pro.nameAr, pro.nameEn) }))}
                />
              )}
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
