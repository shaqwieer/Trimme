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

type RatingInputProps = {
  name: string;
  value?: number;
  onValueChange?: (value: number) => void;
  legend?: string;
  required?: boolean;
};

/** 1–5 star input on native radios (arrow keys, forms, screen readers work natively). */
export function RatingInput({ name, value, onValueChange, legend, required }: RatingInputProps) {
  const t = useTranslations('ui.rating');
  return (
    <fieldset className="flex min-w-0 flex-col gap-2">
      <legend className="text-label font-bold text-text-primary">{legend ?? t('input')}</legend>
      <div className="flex gap-1">
        {[1, 2, 3, 4, 5].map((star) => (
          <label
            key={star}
            className="relative flex size-11 cursor-pointer items-center justify-center rounded-field has-focus-visible:shadow-[var(--focus-ring)]"
          >
            <input
              type="radio"
              name={name}
              value={star}
              required={required}
              checked={value === undefined ? undefined : value === star}
              onChange={() => onValueChange?.(star)}
              className="sr-only"
              aria-label={t('stars', { value: star })}
            />
            <Icon
              name="star"
              className={cn(
                'size-7',
                value !== undefined && star <= value ? 'fill-rating text-rating' : 'text-border-strong',
              )}
            />
          </label>
        ))}
      </div>
    </fieldset>
  );
}
