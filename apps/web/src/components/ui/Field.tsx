import type { ReactNode } from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { Icon } from './icons';

export type FieldState = { invalid: boolean; describedBy: string | undefined };

type FieldProps = {
  id: string;
  label: ReactNode;
  /** Visually hide the label (it stays available to assistive technology). */
  hideLabel?: boolean;
  optional?: boolean;
  helper?: ReactNode;
  /** Already-translated error message; its presence marks the field invalid. */
  error?: ReactNode;
  className?: string;
  children: (state: FieldState) => ReactNode;
};

/**
 * Label + control + helper/error wiring (design ds-components 397–426).
 * The message sits directly under the field and is linked through aria-describedby.
 */
export function Field({ id, label, hideLabel, optional, helper, error, className, children }: FieldProps) {
  const t = useTranslations('ui');
  const helperId = helper ? `${id}-helper` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [errorId, helperId].filter(Boolean).join(' ') || undefined;

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <label htmlFor={id} className={cn('text-label font-bold text-text-primary', hideLabel && 'sr-only')}>
        {label}
        {optional && <span className="ms-1 font-medium text-text-tertiary">{t('optional')}</span>}
      </label>
      {children({ invalid: Boolean(error), describedBy })}
      {error && (
        <p id={errorId} className="flex items-center gap-1.5 text-helper font-medium text-danger-700">
          <Icon name="alert" className="size-3.5" />
          {error}
        </p>
      )}
      {helper && !error && (
        <p id={helperId} className="flex items-center gap-1.5 text-helper text-text-tertiary">
          <Icon name="info" className="size-3.5" />
          {helper}
        </p>
      )}
    </div>
  );
}

/** Shared control styling: 46px, 1.5px border, radius 10; soft brand ring on focus; red/green states. */
export function controlClasses({
  invalid = false,
  valid = false,
}: { invalid?: boolean; valid?: boolean } = {}) {
  return cn(
    'w-full min-h-[46px] rounded-field border-[1.5px] bg-surface px-3.5 text-input text-text-primary transition-[border-color,box-shadow]',
    'placeholder:text-text-placeholder focus-visible:border-brand-500 focus-visible:shadow-[0_0_0_3px_var(--color-brand-100)] focus-visible:rounded-field',
    'disabled:cursor-not-allowed disabled:bg-bg-muted disabled:text-text-disabled',
    invalid ? 'border-danger-500 bg-danger-surface' : valid ? 'border-success-500' : 'border-border-input',
  );
}
