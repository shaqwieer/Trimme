import { useLocale, useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { type AppLocale, formatNumber, formatRating } from '@/lib/i18n/format';
import { Icon } from './icons';

type RatingStarsProps = {
  value: number;
  count?: number;
  size?: 'sm' | 'md';
  showValue?: boolean;
  className?: string;
};

/** Read-only rating: stars + numeric value (+ review count). One accessible label for the group. */
export function RatingStars({ value, count, size = 'sm', showValue = true, className }: RatingStarsProps) {
  const t = useTranslations('ui.rating');
  const locale = useLocale() as AppLocale;
  const rounded = Math.round(value);
  const formatted = formatRating(value, locale);

  return (
    <span
      role="img"
      aria-label={t('value', { value: formatted })}
      className={cn('inline-flex items-center gap-1.5', className)}
    >
      <span aria-hidden="true" className="flex">
        {[1, 2, 3, 4, 5].map((star) => (
          <Icon
            key={star}
            name="star"
            className={cn(
              size === 'sm' ? 'size-3.5' : 'size-[18px]',
              star <= rounded ? 'fill-rating text-rating' : 'text-border-strong',
            )}
          />
        ))}
      </span>
      {showValue && (
        <span aria-hidden="true" className="font-latin text-helper font-semibold text-text-strong">
          {formatted}
        </span>
      )}
      {count !== undefined && (
        <span aria-hidden="true" className="text-helper text-text-tertiary">
          {t('count', { count: formatNumber(count, locale) })}
        </span>
      )}
    </span>
  );
}
