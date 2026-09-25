'use client';

import type { ChangeEvent, ReactNode } from 'react';
import { cn } from '@/lib/cn';
import { Icon } from './icons';

/*
 * Interactive selection controls. They attach their own event handlers, so they must be Client
 * Components (Server Components cannot pass handlers to DOM elements). Server-safe siblings
 * (RadioCard, TagChip) live in ./selection.
 */

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
