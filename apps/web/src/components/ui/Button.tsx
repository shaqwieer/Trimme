import type { ButtonHTMLAttributes, ComponentProps, ReactNode } from 'react';
import { Link } from '@/i18n/navigation';
import { cn } from '@/lib/cn';
import { type DesignIconName, Icon } from './icons';

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'danger' | 'dangerSolid' | 'ghost';
export type ButtonSize = 'lg' | 'md' | 'sm' | 'xs';

const base =
  'relative inline-flex select-none items-center justify-center gap-2 whitespace-nowrap font-bold transition-[background-color,border-color,box-shadow,transform] duration-150 disabled:cursor-not-allowed aria-disabled:cursor-not-allowed';

/** Design ds-components 379–395 and foundations 3616–3629. */
const variants: Record<ButtonVariant, string> = {
  primary:
    'bg-navy-900 text-on-navy hover:bg-navy-800 hover:shadow-button-hover active:scale-[.98] active:bg-navy-950 disabled:bg-bg-app disabled:text-text-disabled disabled:shadow-none',
  secondary:
    'bg-brand-100 text-brand-700 hover:bg-brand-150 active:scale-[.98] disabled:bg-bg-app disabled:text-text-disabled',
  outline:
    'border-[1.5px] border-border-strong bg-surface text-text-strong hover:border-brand-500 active:scale-[.98] disabled:bg-bg-app disabled:text-text-disabled',
  danger:
    'bg-danger-50 text-danger-700 hover:bg-danger-100 active:scale-[.98] disabled:bg-bg-app disabled:text-text-disabled',
  dangerSolid:
    'bg-danger-500 text-on-navy hover:bg-danger-700 active:scale-[.98] disabled:bg-bg-app disabled:text-text-disabled',
  ghost: 'text-text-strong hover:bg-brand-100 active:scale-[.98] disabled:text-text-disabled',
};

const sizes: Record<ButtonSize, string> = {
  lg: 'min-h-[46px] px-5 rounded-button text-button',
  md: 'min-h-11 px-[18px] rounded-button text-[0.875rem]',
  sm: 'min-h-11 px-4 rounded-[11px] text-[0.84375rem]',
  xs: 'min-h-9 px-3.5 rounded-field text-label',
};

const iconSizes: Record<ButtonSize, string> = {
  lg: 'size-[18px]',
  md: 'size-[18px]',
  sm: 'size-4',
  xs: 'size-4',
};

/** Class list shared by <Button> and <ButtonLink>, so links that look like buttons stay identical. */
export function buttonClasses({
  variant = 'primary',
  size = 'lg',
  fullWidth = false,
}: {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
} = {}) {
  return cn(base, variants[variant], sizes[size], fullWidth && 'w-full');
}

type SharedProps = {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
  /** Leading icon (precedes the text in reading order, so it sits on the right in Arabic). */
  icon?: DesignIconName;
  iconEnd?: DesignIconName;
};

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> &
  SharedProps & {
    /** Shows a spinner, sets aria-busy and blocks further clicks (design "جارٍ التأكيد"). */
    loading?: boolean;
    loadingText?: ReactNode;
  };

export function Button({
  variant,
  size = 'lg',
  fullWidth,
  icon,
  iconEnd,
  loading = false,
  loadingText,
  disabled,
  className,
  children,
  type = 'button',
  ...props
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cn(
        buttonClasses({ variant, size, fullWidth }),
        loading && 'disabled:bg-navy-800 disabled:text-on-navy',
        className,
      )}
      {...props}
    >
      {loading ? (
        <span
          aria-hidden="true"
          className="size-[15px] animate-spin rounded-full border-2 border-on-navy/35 border-t-on-navy"
        />
      ) : (
        icon && <Icon name={icon} className={iconSizes[size]} />
      )}
      {loading && loadingText ? loadingText : children}
      {!loading && iconEnd && <Icon name={iconEnd} className={iconSizes[size]} />}
    </button>
  );
}

type ButtonLinkProps = Omit<ComponentProps<typeof Link>, 'className'> & SharedProps & { className?: string };

/** A locale-aware link styled as a button (navigation must be a link, not a button). */
export function ButtonLink({
  variant,
  size = 'lg',
  fullWidth,
  icon,
  iconEnd,
  className,
  children,
  ...props
}: ButtonLinkProps) {
  return (
    <Link className={cn(buttonClasses({ variant, size, fullWidth }), className)} {...props}>
      {icon && <Icon name={icon} className={iconSizes[size]} />}
      {children}
      {iconEnd && <Icon name={iconEnd} className={iconSizes[size]} />}
    </Link>
  );
}

type IconButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  icon: DesignIconName;
  /** Required accessible name — icon-only controls must always be labelled. */
  label: string;
  variant?: 'outline' | 'secondary' | 'ghost' | 'onDark';
  size?: 'lg' | 'md' | 'sm';
  pressed?: boolean;
};

const iconButtonVariants = {
  outline: 'border-[1.5px] border-border-strong bg-surface text-text-strong hover:border-brand-500',
  secondary: 'bg-brand-100 text-brand-700 hover:bg-brand-150',
  ghost: 'text-text-strong hover:bg-brand-100',
  onDark: 'text-on-navy-muted hover:bg-on-navy-subtle hover:text-on-navy',
} as const;

const iconButtonSizes = {
  lg: 'size-[46px] rounded-button',
  md: 'size-11 rounded-button',
  sm: 'size-9 rounded-field',
} as const;

/** Square icon-only button (46/44/36px). Always has an accessible label. */
export function IconButton({
  icon,
  label,
  variant = 'ghost',
  size = 'md',
  pressed,
  className,
  type = 'button',
  ...props
}: IconButtonProps) {
  return (
    <button
      type={type}
      aria-label={label}
      aria-pressed={pressed}
      className={cn(
        'inline-flex shrink-0 items-center justify-center transition-colors disabled:cursor-not-allowed disabled:opacity-60',
        iconButtonVariants[variant],
        iconButtonSizes[size],
        className,
      )}
      {...props}
    >
      <Icon name={icon} className={size === 'sm' ? 'size-4' : 'size-[19px]'} />
    </button>
  );
}
