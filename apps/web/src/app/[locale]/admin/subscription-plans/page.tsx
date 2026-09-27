import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { PlanOrderButtons } from '@/components/subscriptions/PlanAdmin';
import { PlanStatusBadge } from '@/components/subscriptions/PlanStatusBadge';
import { longDate } from '@/components/subscriptions/periods';
import { ButtonLink } from '@/components/ui/Button';
import { ResponsiveTable } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * SuperAdmin plan catalogue (DV-A10, absent from the design): every plan with its price in force, a scheduled price
 * if any, its billing period and status, in display order. Nothing about a plan is hardcoded (R-NEG-08).
 */
export default async function SubscriptionPlansPage({
  params,
}: PageProps<'/[locale]/admin/subscription-plans'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'subscriptionPlans' });
  const tb = await getTranslations({ locale: lang, namespace: 'billing' });

  return (
    <AdminFrame
      locale={locale}
      path="/admin/subscription-plans"
      title={t('title')}
      permission="SuperAdmin.SubscriptionPlans.Manage"
    >
      {async () => {
        const api = await getServerApi();
        const { data: plans } = await api.GET('/api/v1/admin/subscription-plans');
        if (!plans) return <ErrorState />;
        const orderable = plans.filter((p) => p.status !== 'Archived').map((p) => p.id);

        return (
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <p className="max-w-[720px] text-body text-text-secondary">{t('intro')}</p>
              <ButtonLink href="/admin/subscription-plans/new" size="md" icon="plus">
                {t('add')}
              </ButtonLink>
            </div>
            {plans.length === 0 ? (
              <EmptyState icon="tag" title={t('empty.title')} body={t('empty.body')} />
            ) : (
              <ResponsiveTable
                caption={t('caption')}
                rows={plans}
                rowKey={(row) => row.id}
                columns={[
                  {
                    key: 'plan',
                    header: t('columns.plan'),
                    mobile: 'primary',
                    cell: (row) => (
                      <Link
                        href={`/admin/subscription-plans/${row.id}`}
                        className="font-bold text-text-link hover:underline"
                      >
                        {localizedName(lang, row.nameAr, row.nameEn)}
                      </Link>
                    ),
                  },
                  {
                    key: 'price',
                    header: t('columns.price'),
                    cell: (row) => (
                      <span className="flex flex-col">
                        <span>
                          {row.currentPrice
                            ? formatPrice(row.currentPrice.amount, lang, row.currentPrice.currency)
                            : t('noPrice')}
                        </span>
                        {row.upcomingPrice && (
                          <span className="text-helper text-text-tertiary">
                            {t('upcoming', {
                              price: formatPrice(row.upcomingPrice.amount, lang, row.upcomingPrice.currency),
                              date: longDate(row.upcomingPrice.effectiveFrom, lang),
                            })}
                          </span>
                        )}
                      </span>
                    ),
                  },
                  {
                    key: 'interval',
                    header: t('columns.interval'),
                    cell: (row) => tb(row.intervalUnit, { count: row.intervalCount }),
                  },
                  {
                    key: 'status',
                    header: t('columns.status'),
                    cell: (row) => <PlanStatusBadge status={row.status} />,
                  },
                  {
                    key: 'newShops',
                    header: t('columns.newShops'),
                    cell: (row) => (row.availableToNewShops ? t('offered') : t('notOffered')),
                  },
                  {
                    key: 'subscriptions',
                    header: t('columns.subscriptions'),
                    cell: (row) => row.subscriptionCount,
                  },
                  {
                    key: 'order',
                    header: t('columns.order'),
                    cell: (row) =>
                      row.status === 'Archived' ? null : (
                        <PlanOrderButtons
                          ids={orderable}
                          index={orderable.indexOf(row.id)}
                          name={localizedName(lang, row.nameAr, row.nameEn)}
                        />
                      ),
                  },
                ]}
              />
            )}
          </div>
        );
      }}
    </AdminFrame>
  );
}
