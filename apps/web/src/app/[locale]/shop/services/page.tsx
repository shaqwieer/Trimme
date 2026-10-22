import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
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
 * who does each, turns them on and off, reorders and archives them. The desk has no access; packages stay the admin's.
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
        const { data } = await api.GET('/api/v1/shop/services', { params: { query: { includeArchived } } });
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
          </div>
        );
      }}
    </ShopFrame>
  );
}
