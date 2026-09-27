import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';
import { AdminFrame } from '@/components/admin/AdminFrame';
import { PlanForm } from '@/components/subscriptions/PlanForm';
import { PlanPriceForm, PlanStateActions } from '@/components/subscriptions/PlanAdmin';
import { PlanStatusBadge } from '@/components/subscriptions/PlanStatusBadge';
import { longDate } from '@/components/subscriptions/periods';
import { Breadcrumb, Timeline } from '@/components/ui/data';
import { EmptyState, ErrorState } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatPrice } from '@/lib/i18n/format';
import { todayLocal } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

function Card({
  title,
  body,
  children,
  testId,
}: {
  title: string;
  body?: string;
  children: ReactNode;
  testId?: string;
}) {
  return (
    <section
      className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1"
      data-testid={testId}
    >
      <h2 className="text-h3 font-bold text-navy-900">{title}</h2>
      {body && <p className="text-caption text-text-secondary">{body}</p>}
      {children}
    </section>
  );
}

/**
 * One plan (SuperAdmin): availability actions, the details editor, and the append-only price history with the form to
 * schedule a new version (R-SUB-01/02, DV-A10).
 */
export default async function SubscriptionPlanPage({
  params,
}: PageProps<'/[locale]/admin/subscription-plans/[planId]'>) {
  const { locale, planId } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'subscriptionPlans' });

  return (
    <AdminFrame
      locale={locale}
      path={`/admin/subscription-plans/${planId}`}
      title={t('title')}
      permission="SuperAdmin.SubscriptionPlans.Manage"
    >
      {async () => {
        const api = await getServerApi();
        const { data: plan, response } = await api.GET('/api/v1/admin/subscription-plans/{planId}', {
          params: { path: { planId } },
        });
        if (response.status === 404 || response.status === 400)
          return <EmptyState icon="tag" title={t('empty.title')} />;
        if (!plan) return <ErrorState />;

        const today = todayLocal();
        const name = localizedName(lang, plan.nameAr, plan.nameEn);
        return (
          <div className="flex max-w-[1000px] flex-col gap-5">
            <Breadcrumb
              items={[{ label: t('detail.back'), href: '/admin/subscription-plans' }, { label: name }]}
            />
            <section className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6 shadow-e1">
              <div className="flex flex-wrap items-center gap-3">
                <h2 className="text-h2 font-bold text-navy-900">{name}</h2>
                <PlanStatusBadge status={plan.status} />
              </div>
              <p className="text-caption text-text-secondary">{t('detail.actionsBody')}</p>
              <PlanStateActions plan={plan} />
            </section>

            <Card title={t('detail.pricesTitle')} body={t('detail.pricesBody')} testId="plan-prices">
              {plan.prices.length === 0 ? (
                <p className="text-caption text-text-secondary">{t('detail.noPrices')}</p>
              ) : (
                <Timeline
                  items={plan.prices.map((price) => {
                    const state =
                      price.id === plan.currentPrice?.id
                        ? 'current'
                        : price.effectiveFrom > today
                          ? 'scheduled'
                          : 'past';
                    return {
                      id: price.id,
                      tone: state === 'current' ? 'success' : state === 'scheduled' ? 'warning' : 'neutral',
                      title: t('detail.version', {
                        number: price.versionNumber,
                        price: formatPrice(price.amount, lang, price.currency),
                      }),
                      meta: `${t('detail.effective', { date: longDate(price.effectiveFrom, lang) })} · ${t(`detail.${state}`)}`,
                    };
                  })}
                />
              )}
              {plan.status !== 'Archived' && <PlanPriceForm plan={plan} today={today} />}
            </Card>

            <Card title={t('detail.detailsTitle')}>
              <PlanForm plan={plan} />
            </Card>
          </div>
        );
      }}
    </AdminFrame>
  );
}
