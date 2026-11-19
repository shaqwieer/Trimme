import { useLocale, useTranslations } from 'next-intl';
import { InlineAlert } from '@/components/ui/states';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { type AppLocale, formatMonthYear } from '@/lib/i18n/format';

type Usage = components['schemas']['WhatsAppUsageResponse'];

/**
 * This month's WhatsApp messages to the shop's customers against the plan's allowance (D-149, the client's 6-5): "١٢٠ من
 * ٥٠٠" with a bar, unlimited (no allowance), or none (0). At the limit, notices continue in the app and WhatsApp
 * resumes next month. Only messages to customers count. Shown on the owner's and the admin's subscription page.
 */
export function MessageUsage({ usage }: { usage: Usage }) {
  const t = useTranslations('messageUsage');
  const locale = useLocale() as AppLocale;
  const { used, allowance } = usage;
  const percent = allowance ? Math.min(100, Math.round((100 * used) / allowance)) : 0;
  const reached = allowance !== null && allowance > 0 && used >= allowance;

  return (
    <div
      className="flex flex-col gap-2"
      data-testid="message-usage"
      data-used={used}
      data-allowance={allowance ?? 'unlimited'}
    >
      <p className="text-helper text-text-tertiary">
        {t('month', { month: formatMonthYear(usage.month, locale) })}
      </p>
      {allowance === null ? (
        <>
          <p className="text-label font-bold text-text-primary">{t('sent', { used })}</p>
          <p className="text-helper text-text-secondary">{t('unlimited')}</p>
        </>
      ) : allowance === 0 ? (
        <p className="text-label text-text-secondary">{t('none')}</p>
      ) : (
        <>
          <p className="text-h3 font-bold text-text-primary">{t('of', { used, allowance })}</p>
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
          {reached && <InlineAlert tone="warning" title={t('reached')} />}
        </>
      )}
      <p className="text-helper text-text-tertiary">{t('note')}</p>
    </div>
  );
}
