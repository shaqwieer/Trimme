import type { Metadata } from 'next';
import { getTranslations } from 'next-intl/server';
import { ShopFrame } from '@/components/shop/ShopFrame';
import { longDate } from '@/components/subscriptions/periods';
import { StatusBadge } from '@/components/ui/Badge';
import { EmptyState, InlineAlert } from '@/components/ui/states';
import { asLocale } from '@/i18n/routing';
import { getServerApi } from '@/lib/api/server';
import { formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const metadata: Metadata = { robots: { index: false, follow: false } };

/**
 * The shop's subscription (s-services side column, D-014): status, plan, end date and days left, progress through the
 * current period, a warning when it is expiring, expired or suspended, and the renewal history. Read-only — renewals
 * are recorded by the platform team and there is no payment in v1.
 */
export default async function ShopSubscriptionPage({ params }: PageProps<'/[locale]/shop/subscription'>) {
  const { locale } = await params;
  const lang = asLocale(locale);
  const t = await getTranslations({ locale: lang, namespace: 'shopSubscription' });

  return (
    <ShopFrame locale={locale} path="/shop/subscription" title={t('title')}>
      {async (me) => {
        if (!me.permissions.includes('Shop.Subscription.Read'))
          return <EmptyState icon="shield" title={t('title')} />;
        const api = await getServerApi();
        const { data: mine } = await api.GET('/api/v1/shop/subscription');
        if (!mine) return <EmptyState icon="shield" title={t('title')} />;

        const warn = mine.status !== 'Active';
        return (
          <div className="grid max-w-[1000px] gap-5 lg:grid-cols-[1.5fr_1fr]">
            <div className="flex flex-col gap-5">
              {warn && (
                <InlineAlert
                  tone={mine.status === 'ExpiringSoon' ? 'warning' : 'danger'}
                  title={
                    mine.status === 'ExpiringSoon' || mine.status === 'Active'
                      ? t('warning.ExpiringSoon', { days: mine.daysRemaining })
                      : t(`warning.${mine.status}`)
                  }
                >
                  <p>{mine.hiddenFromDiscovery ? t('warning.hiddenBody') : t('warning.soonBody')}</p>
                  <p className="font-semibold">{t('warning.contact')}</p>
                </InlineAlert>
              )}
              <section
                className="flex flex-col gap-4 rounded-card bg-navy-900 p-6 text-on-navy shadow-e2"
                data-testid="shop-subscription-card"
                aria-labelledby="shop-subscription-plan"
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="text-helper font-bold tracking-wide text-on-navy-muted">
                    {t('eyebrow')}
                  </span>
                  <StatusBadge kind="subscription" status={mine.status} />
                </div>
                {mine.planNameAr ? (
                  <>
                    <h2 id="shop-subscription-plan" className="text-h2 font-bold">
                      {localizedName(lang, mine.planNameAr, mine.planNameEn ?? null)}
                    </h2>
                    {mine.endDate && (
                      <p className="text-body">
                        {mine.daysRemaining > 0
                          ? t('ends', { date: longDate(mine.endDate, lang), days: mine.daysRemaining })
                          : t('ended', { date: longDate(mine.endDate, lang) })}
                      </p>
                    )}
                    <div className="flex flex-col gap-1.5">
                      <div
                        role="progressbar"
                        aria-label={t('progress', { percent: mine.elapsedPercent })}
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-valuenow={mine.elapsedPercent}
                        className="h-2 overflow-hidden rounded-full bg-navy-800"
                      >
                        <div
                          className="h-full rounded-full bg-brand-300"
                          style={{ width: `${mine.elapsedPercent}%` }}
                        />
                      </div>
                      <div className="flex justify-between text-helper text-on-navy-muted">
                        <span>
                          {t('start')}: {mine.startDate ? longDate(mine.startDate, lang) : '—'}
                        </span>
                        <span>
                          {t('end')}: {mine.endDate ? longDate(mine.endDate, lang) : '—'}
                        </span>
                      </div>
                    </div>
                  </>
                ) : (
                  <h2 id="shop-subscription-plan" className="text-body">
                    {t('none')}
                  </h2>
                )}
                <p className="text-helper text-on-navy-muted">{t('note')}</p>
              </section>
            </div>
            <section
              className="flex flex-col gap-3 rounded-card border border-border bg-surface p-6 shadow-e1"
              aria-labelledby="shop-subscription-history"
            >
              <h2 id="shop-subscription-history" className="text-h3 font-bold text-navy-900">
                {t('historyTitle')}
              </h2>
              {mine.renewals.length === 0 ? (
                <p className="text-caption text-text-secondary">{t('historyEmpty')}</p>
              ) : (
                <ul className="flex flex-col divide-y divide-border-row" data-testid="shop-renewals">
                  {mine.renewals.map((r) => (
                    <li key={`${r.periodStart}-${r.periodEnd}`} className="flex flex-col gap-0.5 py-2.5">
                      <span className="text-caption font-semibold text-text-primary">
                        {t('historyItem', {
                          start: longDate(r.periodStart, lang),
                          end: longDate(r.periodEnd, lang),
                        })}
                      </span>
                      <span className="text-helper text-text-secondary">
                        {t('historyPlan', {
                          plan: localizedName(lang, r.planNameAr, r.planNameEn),
                          price: formatPrice(r.amount, lang, r.currency),
                        })}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        );
      }}
    </ShopFrame>
  );
}
