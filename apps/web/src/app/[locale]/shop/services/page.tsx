import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopCatalogList } from '@/components/catalog/ShopCatalogLists';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { ButtonLink } from '@/components/ui/Button';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The shop's own services and packages (s-services, corrected per DV-S03): the shop sets names, prices and durations,
 * turns items on and off, reorders and archives them. Tabs live in the URL (`?tab=packages`).
 */
export default async function ShopServicesPage({
  params,
  searchParams,
}: PageProps<'/[locale]/shop/services'>) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  const t = await getTranslations({ locale: asLocale(locale), namespace: 'shopServices' });
  const tab = firstParam(query.tab) === 'packages' ? 'packages' : 'services';
  const includeArchived = firstParam(query.archived) === '1';

  return (
    <ShopFrame locale={locale} path="/shop/services" title={t('title')}>
      {async (me) => {
        const api = await getServerApi();
        const canManage = me.permissions.includes('Shop.Services.Manage');
        const { data } =
          tab === 'services'
            ? await api.GET('/api/v1/shop/services', { params: { query: { includeArchived } } })
            : await api.GET('/api/v1/shop/packages', { params: { query: { includeArchived } } });
        if (!data) {
          return <EmptyState icon="shield" title={t('empty.title')} />;
        }

        const base = `/shop/services?tab=${tab}`;
        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <p className="text-body text-text-secondary">{t('intro')}</p>
            <LinkTabs
              label={t('tabs.label')}
              tabs={[
                { href: '/shop/services', label: t('tabs.services'), active: tab === 'services' },
                {
                  href: '/shop/services?tab=packages',
                  label: t('tabs.packages'),
                  active: tab === 'packages',
                },
              ]}
            />
            <div className="flex flex-wrap items-center justify-between gap-3">
              <Link
                href={includeArchived ? base : `${base}&archived=1`}
                className="text-caption font-semibold text-text-link hover:underline"
              >
                {includeArchived ? t('hideArchived') : t('showArchived')}
              </Link>
              {canManage && (
                <ButtonLink
                  href={tab === 'services' ? '/shop/services/new' : '/shop/packages/new'}
                  size="md"
                  icon="plus"
                >
                  {tab === 'services' ? t('add') : t('addPackage')}
                </ButtonLink>
              )}
            </div>
            <section className="rounded-card border border-border bg-surface px-6 shadow-e1">
              {data.length === 0 ? (
                <div className="py-6">
                  <EmptyState
                    icon="tag"
                    title={tab === 'services' ? t('empty.title') : t('emptyPackages.title')}
                    body={tab === 'services' ? t('empty.body') : t('emptyPackages.body')}
                  />
                </div>
              ) : (
                <ShopCatalogList kind={tab} items={data} canManage={canManage} />
              )}
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
