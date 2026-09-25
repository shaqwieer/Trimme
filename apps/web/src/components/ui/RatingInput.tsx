'use client';

import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { Icon } from './icons';

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
