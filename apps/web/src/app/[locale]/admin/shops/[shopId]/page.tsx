import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { EditablePolicyForm } from '@/components/admin/EditablePolicyForm';
import { ProfessionalStatusBadge } from '@/components/admin/ProfessionalStatusBadge';
import { InviteShopUserForm, ShopStatusActions } from '@/components/admin/ShopForms';
import { ShopStatusBadge } from '@/components/admin/ShopStatusBadge';
import { CatalogStatusBadge } from '@/components/catalog/CatalogStatusBadge';
import { ShopImagesEditor } from '@/components/shops/ShopImagesEditor';
import { ShopLocationEditor } from '@/components/shops/ShopLocationEditor';
import { ShopProfileEditor } from '@/components/shops/ShopProfileEditor';
import { ShopSubscriptionTab } from '@/components/subscriptions/ShopSubscriptionTab';
import { Avatar } from '@/components/ui/Avatar';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { Breadcrumb } from '@/components/ui/data';
import { LinkTabs } from '@/components/ui/LinkTabs';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { firstParam } from '@/lib/auth/paths';
import { formatDate, formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

const TABS = ['profile', 'location', 'users', 'professionals', 'services', 'subscription'] as const;
type Tab = (typeof TABS)[number];

function Card({ title, children, testId }: { title: string; children: ReactNode; testId?: string }) {
  return (
    <section
      className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1"
      data-testid={testId}
    >
      <h2 className="text-h3 font-bold text-navy-900">{title}</h2>
      {children}
    </section>
  );
}

/**
 * Admin shop detail (DV-A06): status, then tabs for the public profile and images with the shop-edit policy, the
 * exact location (pin picker), accounts, the shop's professionals, its services (added and edited here, D-127) and its
 * subscription. Tab state lives in the URL (`?tab=`).
 */
export default async function AdminShopPage({
  params,
  searchParams,
}: PageProps<'/[locale]/admin/shops/[shopId]'>) {
  const [{ locale, shopId }, query] = await Promise.all([params, searchParams]);
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'adminShops' });
  const requested = firstParam(query.tab);
  const tab: Tab = TABS.includes(requested as Tab) ? (requested as Tab) : 'profile';

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
        const canEdit = me.permissions.includes('Admin.Shops.Edit');
        const mode = { kind: 'admin' as const, shopId: shop.id };
        const tabHref = (target: Tab) => `/admin/shops/${shop.id}?tab=${target}`;

        let content: ReactNode;
        if (tab === 'profile') {
          content = (
            <>
              <Card title={t('sections.profile')}>
                <ShopProfileEditor mode={mode} profile={shop} canEdit={canEdit} />
              </Card>
              <Card title={t('sections.images')}>
                <ShopImagesEditor mode={mode} profile={shop} canEdit={canEdit} />
              </Card>
              {canEdit && (
                <Card title={t('sections.policy')}>
                  <EditablePolicyForm shopId={shop.id} editableFields={shop.editableFields} />
                </Card>
              )}
            </>
          );
        } else if (tab === 'location') {
          content = (
            <Card title={t('sections.location')} testId="shop-location-card">
              <ShopLocationEditor mode={mode} location={shop.location} canEdit={canEdit} locale={lang} />
            </Card>
          );
        } else if (tab === 'users') {
          content = me.permissions.includes('Admin.Shops.ManageAccount') ? (
            <Card title={t('detail.invite.title')}>
              <p className="text-caption text-text-secondary">{t('detail.invite.body')}</p>
              <InviteShopUserForm shopId={shop.id} />
            </Card>
          ) : (
            <EmptyState icon="shield" title={t('detail.invite.title')} />
          );
        } else if (tab === 'subscription') {
          content = <ShopSubscriptionTab shopId={shop.id} permissions={me.permissions} />;
        } else if (tab === 'services') {
          const { data: services } = me.permissions.includes('Admin.ShopServices.View')
            ? await api.GET('/api/v1/admin/services', {
                params: { query: { shopId: shop.id, pageSize: 100 } },
              })
            : { data: undefined };
          const live = (services?.items ?? []).filter((service) => !service.isArchived);
          const canManage = me.permissions.includes('Admin.ShopServices.Manage');
          content = (
            <Card title={t('services.title')} testId="shop-services-card">
              <p className="text-caption text-text-secondary">{t('services.body')}</p>
              {live.length > 0 ? (
                <ul className="flex flex-col divide-y divide-border-row">
                  {live.map((service) => (
                    <li key={service.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-3">
                      <Link
                        href={`/admin/services/${service.id}`}
                        className="min-w-0 flex-1 font-bold text-text-link hover:underline"
                      >
                        {localizedName(lang, service.nameAr, service.nameEn)}
                      </Link>
                      <span className="font-latin text-label font-bold text-navy-900">
                        {formatPrice(service.price, lang, service.currency)}
                      </span>
                      <span className="text-helper text-text-secondary">
                        {formatDurationMinutes(service.durationMinutes, lang)}
                      </span>
                      <CatalogStatusBadge item={service} />
                    </li>
                  ))}
                </ul>
              ) : (
                <EmptyState icon="tag" title={t('services.empty')} body={t('services.emptyBody')} />
              )}
              {canManage && (
                <ButtonLink
                  href={`/admin/services/new?shopId=${shop.id}`}
                  size="md"
                  icon="plus"
                  className="self-start"
                >
                  {t('services.add')}
                </ButtonLink>
              )}
            </Card>
          );
        } else {
          const { data: professionals } = me.permissions.includes('Admin.Professionals.View')
            ? await api.GET('/api/v1/admin/professionals', {
                params: { query: { shopId: shop.id, pageSize: 100 } },
              })
            : { data: undefined };
          content = (
            <Card title={t('professionals.title')}>
              <p className="text-caption text-text-secondary">{t('professionals.body')}</p>
              {professionals && professionals.items.length > 0 ? (
                <ul className="flex flex-col divide-y divide-border-row">
                  {professionals.items.map((pro) => {
                    const proName = lang === 'ar' ? pro.nameAr : pro.nameEn;
                    return (
                      <li key={pro.id} className="flex items-center gap-3 py-3">
                        <Avatar name={proName} src={pro.avatarUrl ?? undefined} size="sm" />
                        <Link
                          href={`/admin/professionals/${pro.id}`}
                          className="font-bold text-text-link hover:underline"
                        >
                          {proName}
                        </Link>
                        <ProfessionalStatusBadge status={pro.status} />
                      </li>
                    );
                  })}
                </ul>
              ) : (
                <EmptyState icon="users" title={t('professionals.empty')} />
              )}
              {me.permissions.includes('Admin.Professionals.Create') && (
                <ButtonLink
                  href={`/admin/professionals/new?shopId=${shop.id}`}
                  size="md"
                  icon="plus"
                  className="self-start"
                >
                  {t('professionals.add')}
                </ButtonLink>
              )}
            </Card>
          );
        }

        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <Breadcrumb items={[{ label: t('detail.back'), href: '/admin/shops' }, { label: name }]} />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <div className="flex flex-wrap items-center gap-3">
                <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
                <ShopStatusBadge status={shop.status} />
                {shop.isVerified && (
                  <Badge tone="success" dot={false}>
                    {t('verifiedBadge')}
                  </Badge>
                )}
              </div>
              <dl className="grid gap-3 text-caption sm:grid-cols-4">
                <div>
                  <dt className="text-helper text-text-tertiary">{t('columns.slug')}</dt>
                  <dd className="font-latin font-semibold text-text-primary" dir="ltr">
                    {shop.slug}
                  </dd>
                </div>
                <div>
                  <dt className="text-helper text-text-tertiary">{t('columns.district')}</dt>
                  <dd className="font-semibold text-text-primary">{shop.location?.district ?? '—'}</dd>
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
              {me.permissions.includes('Admin.Bookings.View') && (
                <ButtonLink
                  href={`/admin/bookings?shop=${shop.id}`}
                  variant="outline"
                  size="md"
                  icon="calendar"
                  className="self-start"
                >
                  {t('detail.bookings')}
                </ButtonLink>
              )}
            </section>

            <LinkTabs
              label={t('tabs.label')}
              tabs={TABS.map((target) => ({
                href: tabHref(target),
                label: t(`tabs.${target}`),
                active: target === tab,
              }))}
            />
            {content}
          </div>
        );
      }}
    </AdminFrame>
  );
}
