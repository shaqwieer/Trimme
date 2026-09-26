import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { InviteShopUserForm, ShopStatusActions } from '@/components/admin/ShopForms';
import { ShopStatusBadge } from '@/components/admin/ShopStatusBadge';
import { Breadcrumb } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatDate } from '@/lib/i18n/format';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/** Admin shop detail (DV-A06, first slice): status actions and account invitations. Profile and location in Phase 06. */
export default async function AdminShopPage({ params }: PageProps<'/[locale]/admin/shops/[shopId]'>) {
  const { locale, shopId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminShops' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/shops/${shopId}`}
      title={t('title')}
      permission="Admin.Shops.View"
    >
      {async (me) => {
        const api = await getServerApi();
        const { data: shop, response } = await api.GET('/api/v1/admin/shops/{shopId}', {
          params: { path: { shopId } },
        });

        if (response.status === 404 || response.status === 400) {
          return (
            <EmptyState icon="store" title={t('detail.notFoundTitle')} body={t('detail.notFoundBody')} />
          );
        }
        if (!shop) return <ErrorState />;

        const name = lang === 'ar' ? shop.nameAr : shop.nameEn;
        return (
          <div className="flex max-w-[900px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('detail.back'), href: '/admin/shops' }, { label: name }]} />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <div className="flex flex-wrap items-center gap-3">
                <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
                <ShopStatusBadge status={shop.status} />
              </div>
              <dl className="grid gap-3 text-caption sm:grid-cols-3">
                <div>
                  <dt className="text-helper text-text-tertiary">{t('columns.slug')}</dt>
                  <dd className="font-latin font-semibold text-text-primary" dir="ltr">
                    {shop.slug}
                  </dd>
                </div>
                <div>
                  <dt className="text-helper text-text-tertiary">{t('detail.timeZone')}</dt>
                  <dd className="font-latin font-semibold text-text-primary" dir="ltr">
                    {shop.timeZone}
                  </dd>
                </div>
                <div>
                  <dt className="text-helper text-text-tertiary">{t('detail.created')}</dt>
                  <dd className="font-semibold text-text-primary">{formatDate(shop.createdAt, lang)}</dd>
                </div>
              </dl>
              {me.permissions.includes('Admin.Shops.Suspend') && (
                <ShopStatusActions shopId={shop.id} status={shop.status} />
              )}
            </section>

            {me.permissions.includes('Admin.Shops.ManageAccount') && (
              <section className="flex flex-col gap-3 rounded-card border border-border bg-surface p-6 shadow-e1">
                <h2 className="text-h3 font-bold text-navy-900">{t('detail.invite.title')}</h2>
                <p className="text-caption text-text-secondary">{t('detail.invite.body')}</p>
                <InviteShopUserForm shopId={shop.id} />
              </section>
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
