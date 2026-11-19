import { useLocale, useTranslations } from 'next-intl';
import { longDate } from '@/components/subscriptions/periods';
import { InlineAlert } from '@/components/ui/states';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import type { AppLocale } from '@/lib/i18n/format';

type Features = components['schemas']['ShopPlanFeaturesResponse'];
type Usage = components['schemas']['MarketingUsageResponse'];

/**
 * What the shop's plan gives (D-155, the client's 2-2), on the owner's and the admin's subscription pages: loyalty
 * (included or not), the «ندعوك للعودة» message and after how many days, and this month's win-back messages against
 * the plan's limit — «رسائل الدعوة هالشهر: ١٢٣ من ٥٠٠». The month runs from the subscription's start day; at the limit
 * the rest wait for the next one («وصل الحد»). Booking messages (confirmation, change, cancellation) always go out
 * and are never counted (the client's 12-1).
 */
export function PlanUsage({ features, usage }: { features: Features; usage?: Usage | null }) {
  const t = useTranslations('planUsage');
  const locale = useLocale() as AppLocale;

  return (
    <div className="flex flex-col gap-3" data-testid="plan-usage">
      <ul className="flex flex-col gap-1.5 text-caption">
        <li data-testid="plan-usage-loyalty" data-included={features.includesLoyalty}>
          {features.includesLoyalty
            ? features.loyaltyActive
              ? t('loyaltyActive')
              : t('loyaltyIncluded')
            : t('loyaltyNotIncluded')}
        </li>
        <li data-testid="plan-usage-win-back" data-enabled={features.winBackEnabled}>
          {features.winBackEnabled ? t('winBackOn', { days: features.winBackAfterDays }) : t('winBackOff')}
        </li>
      </ul>
      {features.winBackEnabled && usage && <Counter usage={usage} locale={locale} />}
      <p className="text-helper text-text-tertiary">{t('note')}</p>
    </div>
  );
}

function Counter({ usage, locale }: { usage: Usage; locale: AppLocale }) {
  const t = useTranslations('planUsage');
  const { used, allowance } = usage;
  const percent = allowance ? Math.min(100, Math.round((100 * used) / allowance)) : 0;
  const reached = allowance !== null && used >= allowance;
  const window = t('window', {
    start: longDate(usage.windowStart, locale),
    end: longDate(usage.windowEnd, locale),
  });

  return (
    <div
      className="flex flex-col gap-2"
      data-testid="marketing-usage"
      data-used={used}
      data-allowance={allowance ?? 'unlimited'}
    >
      {allowance === 0 ? (
        <p className="text-label text-text-secondary">{t('none')}</p>
      ) : (
        <>
          <p className="text-h3 font-bold text-text-primary">
            {allowance === null ? t('sent', { used }) : t('of', { used, allowance })}
          </p>
          <p className="text-helper text-text-tertiary">{window}</p>
          {allowance === null ? (
            <p className="text-helper text-text-secondary">{t('unlimited')}</p>
          ) : (
            <div
              role="progressbar"
              aria-label={t('progress', { percent })}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={percent}
              className="h-2 overflow-hidden rounded-full bg-bg-subtle"
            >
              <div
                className={cn('h-full rounded-full', reached ? 'bg-danger-500' : 'bg-brand-500')}
                style={{ width: `${percent}%` }}
              />
            </div>
          )}
          {reached && (
            <InlineAlert tone="warning" title={t('reached')}>
              {t('reachedBody', { date: longDate(usage.windowEnd, locale) })}
            </InlineAlert>
          )}
        </>
      )}
    </div>
  );
}
