import type { ChangeEvent, InputHTMLAttributes, ReactNode } from 'react';
import { cn } from '@/lib/cn';
import { Icon } from './icons';

type RadioCardProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'className' | 'children'> & {
  children: ReactNode;
  /** Trailing content (price, duration…) aligned to the inline end. */
  aside?: ReactNode;
  className?: string;
};

/**
 * Selectable card built on a native radio (keyboard arrows, form submission and screen readers work
 * without JavaScript). Design: service row 515–536 — 1.5px brand border + tint when selected, 22px
 * check circle at the inline start.
 */
export function RadioCard({ children, aside, className, disabled, ...input }: RadioCardProps) {
  return (
    <label
      className={cn(
        'group relative flex cursor-pointer items-center gap-3.5 rounded-card border border-border bg-surface px-4 py-3.5 transition-colors',
        'has-checked:border-[1.5px] has-checked:border-brand-500 has-checked:bg-brand-50',
        'has-focus-visible:shadow-[var(--focus-ring)]',
        disabled && 'cursor-not-allowed opacity-70',
        className,
      )}
    >
      <input type="radio" className="peer sr-only" disabled={disabled} {...input} />
      <span
        aria-hidden="true"
        className="flex size-[22px] shrink-0 items-center justify-center rounded-full border-[1.5px] border-border-strong text-on-navy peer-checked:border-navy-900 peer-checked:bg-navy-900 [&>svg]:invisible peer-checked:[&>svg]:visible"
      >
        <Icon name="check" className="size-[13px]" strokeWidth={2.5} />
      </span>
      <span className="min-w-0 flex-1">{children}</span>
      {aside && <span className="shrink-0 text-end">{aside}</span>}
    </label>
  );
}

type ChipProps = {
  children: ReactNode;
  pressed?: boolean;
  onPressedChange?: (pressed: boolean) => void;
  icon?: Parameters<typeof Icon>[0]['name'];
  className?: string;
};

/** Toggle chip for filters and categories (design results chips 1083–1087): navy fill when on. */
export function Chip({ children, pressed = false, onPressedChange, icon, className }: ChipProps) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      onClick={() => onPressedChange?.(!pressed)}
      className={cn(
        'inline-flex min-h-10 items-center gap-1.5 rounded-button border px-4 text-label font-bold whitespace-nowrap transition-colors',
        pressed
          ? 'border-navy-900 bg-navy-900 text-on-navy'
          : 'border-border-input bg-surface text-text-strong hover:border-brand-500',
        className,
      )}
    >
      {icon && <Icon name={icon} className="size-4" />}
      {children}
    </button>
  );
}

/** Static tag (design 570): services offered, amenities. */
export function TagChip({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-badge bg-bg-subtle px-2 py-1 text-[0.71875rem] text-text-strong',
        className,
      )}
    >
      {children}
    </span>
  );
}

/** Removable chip (design break chip 687). */
export function RemovableChip({
  children,
  onRemove,
  removeLabel,
}: {
  children: ReactNode;
  onRemove: () => void;
  removeLabel: string;
}) {
  return (
    <span className="inline-flex items-center gap-1 rounded-sm border border-border-input bg-surface ps-3 text-helper text-text-strong">
      {children}
      <button
        type="button"
        onClick={onRemove}
        aria-label={removeLabel}
        className="inline-flex size-9 items-center justify-center rounded-sm text-text-tertiary hover:text-danger-700"
      >
        <Icon name="x" className="size-3.5" />
      </button>
    </span>
  );
}

/** Dashed "add" chip (design 689). */
export function AddChip({ children, onClick }: { children: ReactNode; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex min-h-9 items-center gap-1.5 rounded-sm border-[1.5px] border-dashed border-border-dashed px-3 text-helper font-bold text-brand-700 hover:border-brand-500"
    >
      <Icon name="plus" className="size-3.5" />
      {children}
    </button>
  );
}

type SegmentedOption = { value: string; label: ReactNode; icon?: Parameters<typeof Icon>[0]['name'] };

type SegmentedControlProps = {
  legend: string;
  name: string;
  options: SegmentedOption[];
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  className?: string;
};

/**
 * Segmented control (design 447–451: track #F1F5F9, active item white + bold + soft shadow).
 * Native radio group, so arrow keys and RTL behave natively.
 */
export function SegmentedControl({
  legend,
  name,
  options,
  value,
  defaultValue,
  onValueChange,
  className,
}: SegmentedControlProps) {
  return (
    <fieldset className={cn('flex min-w-0 gap-1 rounded-button bg-bg-subtle p-1', className)}>
      <legend className="sr-only">{legend}</legend>
      {options.map((option) => (
        <label
          key={option.value}
          className={cn(
            'relative flex min-h-10 flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-sm px-3 text-[0.875rem] text-text-tertiary transition-colors',
            'has-checked:bg-surface has-checked:font-bold has-checked:text-navy-900 has-checked:shadow-segment',
            'has-focus-visible:shadow-[var(--focus-ring)]',
          )}
        >
          <input
            type="radio"
            name={name}
            value={option.value}
            className="sr-only"
            checked={value === undefined ? undefined : value === option.value}
            defaultChecked={defaultValue === undefined ? undefined : defaultValue === option.value}
            onChange={(event: ChangeEvent<HTMLInputElement>) =>
              event.target.checked && onValueChange?.(option.value)
            }
          />
          {option.icon && <Icon name={option.icon} className="size-4" />}
          {option.label}
        </label>
      ))}
    </fieldset>
  );
}
