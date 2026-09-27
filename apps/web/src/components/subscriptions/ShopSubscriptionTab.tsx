import { getLocale, getTranslations } from 'next-intl/server';
import type { ReactNode } from 'react';
import { StatusBadge } from '@/components/ui/Badge';
import { EmptyState, ErrorState, InlineAlert } from '@/components/ui/states';
import { getServerApi } from '@/lib/api/server';
import { type AppLocale, formatNumber, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { longDate } from './periods';
import { SubscriptionHistory } from './SubscriptionHistory';
import { OverrideForm, RecordPeriodForm, SuspensionControl } from './ShopSubscriptionPanel';

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
 * The admin shop page's subscription tab (spec §15, DV-A11): current status and period, then what the admin's
 * permissions allow — record activation/renewal (Assign/Renew), suspend/reinstate (Suspend), SuperAdmin override —
 * and the complete history. The server enforces every permission; the UI only hides what would be refused.
 */
export async function ShopSubscriptionTab({
  shopId,
  permissions,
}: {
  shopId: string;
  permissions: readonly string[];
}) {
  const locale = (await getLocale()) as AppLocale;
  const t = await getTranslations({ locale, namespace: 'adminSubscriptions.panel' });
  const tTabs = await getTranslations({ locale, namespace: 'adminShops.tabs' });
  if (!permissions.includes('Admin.Subscriptions.View'))
    return <EmptyState icon="shield" title={tTabs('subscription')} />;

  const api = await getServerApi();
  const [{ data: subscription }, { data: plans }] = await Promise.all([
    api.GET('/api/v1/admin/shops/{shopId}/subscription', { params: { path: { shopId } } }),
    api.GET('/api/v1/admin/subscription-plans'),
  ]);
  if (!subscription || !plans) return <ErrorState />;

  const canRecord = permissions.includes(
    subscription.exists ? 'Admin.Subscriptions.Renew' : 'Admin.Subscriptions.Assign',
  );
  return (
    <>
      <Card title={tTabs('subscription')} testId="shop-subscription">
        <div className="flex flex-wrap items-center gap-3">
          <StatusBadge kind="subscription" status={subscription.status} />
          {subscription.isSuspended && subscription.suspensionReason && (
            <span className="text-caption text-text-secondary">
              {t('suspendedBecause', { reason: subscription.suspensionReason })}
            </span>
          )}
        </div>
        {subscription.exists ? (
          <dl className="grid gap-3 text-caption sm:grid-cols-4">
            <div>
              <dt className="text-helper text-text-tertiary">{t('plan')}</dt>
              <dd className="font-semibold text-text-primary">
                {localizedName(locale, subscription.planNameAr ?? '', subscription.planNameEn ?? null)}
              </dd>
            </div>
            <div>
              <dt className="text-helper text-text-tertiary">{t('ends')}</dt>
              <dd className="font-semibold text-text-primary">
                {subscription.endDate ? longDate(subscription.endDate, locale) : '—'}
              </dd>
            </div>
            <div>
              <dt className="text-helper text-text-tertiary">{t('remaining')}</dt>
              <dd className="font-semibold text-text-primary">
                {formatNumber(subscription.daysRemaining, locale, 0)}
              </dd>
            </div>
            <div>
              <dt className="text-helper text-text-tertiary">{t('amount')}</dt>
              <dd className="font-semibold text-text-primary">
                {subscription.currentAmount != null
                  ? formatPrice(subscription.currentAmount, locale, subscription.currency ?? undefined)
                  : '—'}
              </dd>
            </div>
          </dl>
        ) : (
          <InlineAlert tone="info" title={t('none')} />
        )}
      </Card>

      {canRecord && (
        <Card title={subscription.exists ? t('renewTitle') : t('assignTitle')}>
          <RecordPeriodForm
            subscription={subscription}
            plans={plans}
            canOverride={permissions.includes('SuperAdmin.Subscriptions.Override')}
          />
        </Card>
      )}
      {subscription.exists && permissions.includes('Admin.Subscriptions.Suspend') && (
        <Card title={t('suspendTitle')}>
          <SuspensionControl subscription={subscription} />
        </Card>
      )}
      {subscription.exists && permissions.includes('SuperAdmin.Subscriptions.Override') && (
        <Card title={t('overrideTitle')}>
          <OverrideForm subscription={subscription} />
        </Card>
      )}
      {subscription.exists && (
        <Card title={t('historyTitle')} testId="subscription-history">
          <SubscriptionHistory subscription={subscription} />
        </Card>
      )}
    </>
  );
}
