import { useLocale, useTranslations } from 'next-intl';
import { Icon } from '@/components/ui/icons';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';
import { cn } from '@/lib/cn';

/**
 * The stamp card (D-132): one circle per visit the programme needs. A completed visit fills its circle green with a
 * check; the rest stay empty, so the customer sees at a glance how many visits are left before the coupon.
 */
export function LoyaltyStamps({
  required,
  stamps,
  size = 'md',
  className,
}: {
  required: number;
  stamps: number;
  size?: 'sm' | 'md';
  className?: string;
}) {
  const t = useTranslations('loyalty');
  const locale = useLocale() as AppLocale;
  const filled = Math.min(Math.max(stamps, 0), required);

  return (
    <div
      role="img"
      aria-label={t('stampsLabel', {
        filledText: formatNumber(filled, locale),
        requiredText: formatNumber(required, locale),
      })}
      className={cn('flex flex-wrap items-center gap-2', className)}
      data-testid="loyalty-stamps"
      data-filled={filled}
    >
      {Array.from({ length: required }, (_, index) => {
        const done = index < filled;
        return (
          <span
            key={index}
            aria-hidden="true"
            data-stamp={done ? 'filled' : 'empty'}
            className={cn(
              'flex shrink-0 items-center justify-center rounded-full border-2',
              size === 'md' ? 'size-9' : 'size-6',
              done ? 'border-success-500 bg-success-500 text-on-navy' : 'border-border-strong bg-surface',
            )}
          >
            {done && <Icon name="check" className={size === 'md' ? 'size-5' : 'size-3.5'} strokeWidth={3} />}
          </span>
        );
      })}
    </div>
  );
}
