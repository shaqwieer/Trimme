'use client';

import { Minus, Plus } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { type ReactNode, useId } from 'react';
import { cn } from '@/lib/cn';

/**
 * A whole number changed with − and + (D-151: the app's length and points steppers). The value is announced when it
 * changes; each button names what it changes ("Decrease Price"). The range is enforced here: a button at its end is
 * disabled.
 */
export function NumberStepper({
  label,
  hideLabel = false,
  value,
  onChange,
  min,
  max,
  step = 1,
  format = String,
  disabled = false,
  className,
  testId,
}: {
  label: string;
  hideLabel?: boolean;
  value: number;
  onChange: (value: number) => void;
  min: number;
  max: number;
  step?: number;
  /** How the value is written ("٣٠ دقيقة"). */
  format?: (value: number) => ReactNode;
  disabled?: boolean;
  className?: string;
  testId?: string;
}) {
  const t = useTranslations('ui.numberStepper');
  const labelId = useId();
  return (
    <div
      role="group"
      aria-labelledby={labelId}
      className={cn('flex items-center justify-between gap-3', className)}
      data-testid={testId}
    >
      <span id={labelId} className={hideLabel ? 'sr-only' : 'text-caption font-semibold text-text-strong'}>
        {label}
      </span>
      <span className="flex items-center gap-2">
        <StepButton
          label={t('decrease', { label })}
          disabled={disabled || value - step < min}
          onClick={() => onChange(Math.max(min, value - step))}
        >
          <Minus className="size-4" strokeWidth={1.75} aria-hidden="true" />
        </StepButton>
        <output aria-live="polite" className="min-w-16 text-center text-label font-bold text-text-primary">
          {format(value)}
        </output>
        <StepButton
          label={t('increase', { label })}
          disabled={disabled || value + step > max}
          onClick={() => onChange(Math.min(max, value + step))}
        >
          <Plus className="size-4" strokeWidth={1.75} aria-hidden="true" />
        </StepButton>
      </span>
    </div>
  );
}

/**
 * The outline icon button's look (36px, 44px on touch screens). The − is drawn here rather than added to the design's
 * icon map, which every page loads (the bundle budget).
 */
function StepButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="inline-flex size-9 shrink-0 items-center justify-center rounded-field border-[1.5px] border-border-strong bg-surface text-text-strong transition-colors hover:border-brand-500 disabled:cursor-not-allowed disabled:opacity-60 pointer-coarse:size-11"
    >
      {children}
    </button>
  );
}
