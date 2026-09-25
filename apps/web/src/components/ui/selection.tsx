import type { InputHTMLAttributes, ReactNode } from 'react';
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
