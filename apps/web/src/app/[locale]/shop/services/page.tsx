import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ServiceSections } from '@/components/catalog/ServiceSections';
import { ShopPackageList } from '@/components/catalog/ShopPackages';
import { ShopServiceList } from '@/components/catalog/ShopServices';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { ButtonLink } from '@/components/ui/Button';
import { EmptyState, ErrorState, PermissionDenied } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The owner's services (s-services, corrected per DV-S03; back with D-137): the shop sets names, prices, durations and
 * who does each, turns them on and off, reorders and archives them; its own sections (D-149: the tabs of its page); and
 * its packages (D-147; the admin keeps them too).
 * Barbers have no access.
 */
export default async function ShopServicesPage({
  params,
  searchParams,
}: PageProps<'/[locale]/shop/services'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopServices' });
  const includeArchived = firstParam(query.archived) === '1';

  return (
    <ShopFrame locale={locale} path="/shop/services" title={t('title')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Services.Manage')) return <PermissionDenied homeHref="/shop" />;
        const api = await getServerApi();
        const [{ data }, { data: packages }, { data: sections }] = await Promise.all([
          api.GET('/api/v1/shop/services', { params: { query: { includeArchived } } }),
          api.GET('/api/v1/shop/packages', { params: { query: { includeArchived } } }),
          api.GET('/api/v1/shop/service-sections'),
        ]);
        if (!data) return <ErrorState />;

        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <Link
                href={includeArchived ? '/shop/services' : '/shop/services?archived=1'}
                className="text-caption font-semibold text-text-link hover:underline"
              >
                {includeArchived ? t('hideArchived') : t('showArchived')}
              </Link>
              <ButtonLink href="/shop/services/new" size="md" icon="plus">
                {t('add')}
              </ButtonLink>
            </div>
            <section className="rounded-card border border-border bg-surface px-4 shadow-e1 md:px-6">
              {data.length === 0 ? (
                <div className="py-6">
                  <EmptyState icon="tag" title={t('empty.title')} body={t('empty.body')} />
                </div>
              ) : (
                <ShopServiceList items={data} />
              )}
            </section>
            {sections && (
              <section
                className="flex flex-col gap-2 rounded-card border border-border bg-surface px-4 py-4 shadow-e1 md:px-6"
                aria-labelledby="sections-title"
              >
                <h2 id="sections-title" className="text-h3 font-bold text-navy-900">
                  {t('sections.title')}
                </h2>
                <ServiceSections scope={{ kind: 'shop' }} sections={sections} />
              </section>
            )}
            <section
              className="flex flex-col gap-2 rounded-card border border-border bg-surface px-4 py-4 shadow-e1 md:px-6"
              aria-labelledby="packages-title"
            >
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 id="packages-title" className="text-h3 font-bold text-navy-900">
                  {t('packages.title')}
                </h2>
                <ButtonLink href="/shop/packages/new" size="sm" variant="outline" icon="plus">
                  {t('packages.add')}
                </ButtonLink>
              </div>
              <ShopPackageList items={packages ?? []} />
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
