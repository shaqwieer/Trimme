import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { ModerationControl, ServiceOverride } from '@/components/admin/AdminCatalog';
import { CatalogStatusBadge } from '@/components/catalog/CatalogStatusBadge';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** One shop service for the admin: facts, moderation and the audited support override. */
export default async function AdminServicePage({
  params,
}: PageProps<'/[locale]/admin/services/[serviceId]'>) {
  const { locale, serviceId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminServices' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/services/${serviceId}`}
      title={t('title')}
      permission="Admin.ShopServices.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const [{ data: service, response }, { data: categories }] = await Promise.all([
          api.GET('/api/v1/admin/services/{serviceId}', { params: { path: { serviceId } } }),
          api.GET('/api/v1/public/service-categories'),
        ]);
        if (response.status === 404 || response.status === 400)
          return <EmptyState icon="tag" title={t('empty.title')} />;
        if (!service) return <ErrorState />;

        const name = localizedName(lang, service.nameAr, service.nameEn);
        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('detail.back'), href: '/admin/services' }, { label: name }]} />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <div className="flex flex-wrap items-center gap-3">
                <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
                <CatalogStatusBadge item={service} />
              </div>
              <dl className="grid gap-3 text-caption sm:grid-cols-4">
                <div>
                  <dt className="text-helper text-text-tertiary">{t('columns.shop')}</dt>
                  <dd className="font-semibold">
                    <Link href={`/admin/shops/${service.shopId}`} className="text-text-link hover:underline">
                      {lang === 'ar' ? service.shopNameAr : service.shopNameEn}
                    </Link>
                  </dd>
                </div>
                <div>
                  <dt className="text-helper text-text-tertiary">{t('columns.price')}</dt>
                  <dd className="font-semibold text-text-primary">
                    {formatPrice(service.price, lang, service.currency)}
                  </dd>
                </div>
                <div>
                  <dt className="text-helper text-text-tertiary">{t('columns.duration')}</dt>
                  <dd className="font-semibold text-text-primary">
                    {formatDurationMinutes(service.durationMinutes, lang)}
                  </dd>
                </div>
                <div>
                  <dt className="text-helper text-text-tertiary">{t('detail.assignedCount')}</dt>
                  <dd className="font-semibold text-text-primary">{service.assignedProfessionalCount}</dd>
                </div>
              </dl>
            </section>

            {me.permissions.includes('Admin.ShopServices.Moderate') && (
              <section className="flex flex-col gap-3 rounded-card border border-border bg-surface p-6 shadow-e1">
                <h2 className="text-h3 font-bold text-navy-900">{t('detail.moderationTitle')}</h2>
                <p className="text-caption text-text-secondary">{t('detail.moderationBody')}</p>
                <ModerationControl
                  kind="service"
                  id={service.id}
                  hidden={service.moderation === 'Hidden'}
                  reason={service.moderationReason}
                />
              </section>
            )}

            {me.permissions.includes('Admin.ShopServices.SupportOverride') && !service.isArchived && (
              <section
                className="flex flex-col gap-3 rounded-card border border-border bg-surface p-6 shadow-e1"
                data-testid="service-override"
              >
                <h2 className="text-h3 font-bold text-navy-900">{t('detail.overrideTitle')}</h2>
                <p className="text-caption text-text-secondary">{t('detail.overrideBody')}</p>
                <ServiceOverride service={service} categories={categories ?? []} />
              </section>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
