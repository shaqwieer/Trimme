'use client';

import {
  type ChangeEvent,
  type InputHTMLAttributes,
  type ReactNode,
  type Ref,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
  useId,
  useState,
} from 'react';
import { useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { digitsOnly, groupSaudiMobile, normalizeSaudiMobile, SAUDI_DIAL_CODE, toE164 } from './digits';
import { controlClasses, Field } from './Field';
import { Icon } from './icons';

type CommonFieldProps = {
  label: ReactNode;
  hideLabel?: boolean;
  optional?: boolean;
  helper?: ReactNode;
  error?: ReactNode;
  className?: string;
};

type TextFieldProps = CommonFieldProps &
  Omit<InputHTMLAttributes<HTMLInputElement>, 'className'> & {
    valid?: boolean;
    /** Lets react-hook-form focus the first invalid field. */
    ref?: Ref<HTMLInputElement>;
  };

export function TextField({
  label,
  hideLabel,
  optional,
  helper,
  error,
  valid,
  className,
  id,
  ...input
}: TextFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <Field
      id={fieldId}
      label={label}
      hideLabel={hideLabel}
      optional={optional}
      helper={helper}
      error={error}
      className={className}
    >
      {({ invalid, describedBy }) => (
        <div className="relative">
          <input
            id={fieldId}
            aria-invalid={invalid || undefined}
            aria-describedby={describedBy}
            className={cn(controlClasses({ invalid, valid }), valid && 'pe-10')}
            {...input}
          />
          {valid && !invalid && (
            <Icon
              name="check"
              className="pointer-events-none absolute end-3 top-1/2 size-[18px] -translate-y-1/2 text-success-700"
            />
          )}
        </div>
      )}
    </Field>
  );
}

type PhoneFieldProps = CommonFieldProps & {
  name?: string;
  /** National digits (e.g. "512345678"). */
  value?: string;
  defaultValue?: string;
  /** Receives national digits and the E.164 value (null while incomplete/invalid). */
  onValueChange?: (national: string, e164: string | null) => void;
  onBlur?: () => void;
  disabled?: boolean;
  required?: boolean;
  autoFocus?: boolean;
};

/**
 * Saudi mobile number (design 406–410): fixed "+966" segment, 9 digits, always left-to-right,
 * grouped "51 234 5678" while typing; accepts Arabic-Indic digits and pasted +966/05 forms.
 */
export function PhoneField({
  label,
  hideLabel,
  optional,
  helper,
  error,
  className,
  name,
  value,
  defaultValue = '',
  onValueChange,
  onBlur,
  disabled,
  required,
  autoFocus,
}: PhoneFieldProps) {
  const t = useTranslations('ui.phone');
  const id = useId();
  const [internal, setInternal] = useState(() => normalizeSaudiMobile(defaultValue));
  const national = value !== undefined ? normalizeSaudiMobile(value) : internal;

  const onChange = (event: ChangeEvent<HTMLInputElement>) => {
    const next = normalizeSaudiMobile(event.target.value);
    if (value === undefined) setInternal(next);
    onValueChange?.(next, toE164(next));
  };

  return (
    <Field
      id={id}
      label={label}
      hideLabel={hideLabel}
      optional={optional}
      helper={helper}
      error={error}
      className={className}
    >
      {({ invalid, describedBy }) => (
        <div
          dir="ltr"
          className={cn(
            controlClasses({ invalid }),
            'flex items-stretch p-0 focus-within:border-brand-500 focus-within:shadow-[0_0_0_3px_var(--color-brand-100)]',
          )}
        >
          <span
            aria-label={t('countryCode')}
            className="flex items-center rounded-s-[8px] border-e-[1.5px] border-border-input bg-bg-page px-3 font-latin text-[0.84375rem] font-semibold text-text-strong"
          >
            {SAUDI_DIAL_CODE}
          </span>
          <input
            id={id}
            name={name}
            type="tel"
            inputMode="numeric"
            autoComplete="tel-national"
            dir="ltr"
            placeholder={t('placeholder')}
            value={groupSaudiMobile(national)}
            onChange={onChange}
            onBlur={onBlur}
            disabled={disabled}
            required={required}
            autoFocus={autoFocus}
            aria-invalid={invalid || undefined}
            aria-describedby={describedBy}
            className="min-w-0 flex-1 bg-transparent px-3 font-latin text-[0.9375rem] font-medium tracking-[0.06em] text-text-primary placeholder:text-text-placeholder focus-visible:shadow-none"
          />
        </div>
      )}
    </Field>
  );
}

type OtpFieldProps = CommonFieldProps & {
  length?: number;
  value?: string;
  onValueChange?: (code: string) => void;
  onComplete?: (code: string) => void;
  name?: string;
  autoFocus?: boolean;
  disabled?: boolean;
};

/** One-time code (D-037: 6 digits). Single LTR input so paste and SMS/WhatsApp autofill work. */
export function OtpField({
  label,
  hideLabel,
  helper,
  error,
  className,
  length = 6,
  value,
  onValueChange,
  onComplete,
  name,
  autoFocus,
  disabled,
}: OtpFieldProps) {
  const t = useTranslations('ui.otp');
  const id = useId();
  const [internal, setInternal] = useState('');
  const code = value ?? internal;

  const onChange = (event: ChangeEvent<HTMLInputElement>) => {
    const next = digitsOnly(event.target.value, length);
    if (value === undefined) setInternal(next);
    onValueChange?.(next);
    if (next.length === length) onComplete?.(next);
  };

  return (
    <Field
      id={id}
      label={label}
      hideLabel={hideLabel}
      helper={helper ?? t('hint', { length })}
      error={error}
      className={className}
    >
      {({ invalid, describedBy }) => (
        <div className="relative">
          <input
            id={id}
            name={name}
            type="text"
            inputMode="numeric"
            autoComplete="one-time-code"
            dir="ltr"
            maxLength={length}
            value={code}
            onChange={onChange}
            autoFocus={autoFocus}
            disabled={disabled}
            aria-invalid={invalid || undefined}
            aria-describedby={describedBy}
            className={cn(
              controlClasses({ invalid }),
              'text-center font-latin text-[0.9375rem] font-semibold tracking-[0.3em]',
            )}
          />
          {invalid && (
            <Icon
              name="alert"
              className="pointer-events-none absolute end-3 top-1/2 size-[18px] -translate-y-1/2 text-danger-500"
            />
          )}
        </div>
      )}
    </Field>
  );
}

type SearchFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'className'> & {
  label: string;
  className?: string;
  onClear?: () => void;
};

/** Search input with leading icon and a clear button (design 418). The label is visually hidden. */
export function SearchField({ label, className, onClear, value, placeholder, ...input }: SearchFieldProps) {
  const t = useTranslations('ui.search');
  const id = useId();
  const hasValue = typeof value === 'string' && value.length > 0;
  return (
    <div role="search" className={cn('relative', className)}>
      <label htmlFor={id} className="sr-only">
        {label}
      </label>
      <Icon
        name="search"
        className="pointer-events-none absolute start-3.5 top-1/2 size-[18px] -translate-y-1/2 text-text-tertiary"
      />
      <input
        id={id}
        type="search"
        value={value}
        placeholder={placeholder ?? t('placeholder')}
        className={cn(
          controlClasses(),
          'ps-10',
          hasValue && onClear && 'pe-11',
          '[&::-webkit-search-cancel-button]:hidden',
        )}
        {...input}
      />
      {hasValue && onClear && (
        <button
          type="button"
          onClick={onClear}
          aria-label={t('clear')}
          className="absolute end-1 top-1/2 inline-flex size-9 -translate-y-1/2 items-center justify-center rounded-field text-text-tertiary hover:bg-bg-subtle"
        >
          <Icon name="x" className="size-4" />
        </button>
      )}
    </div>
  );
}

type SelectFieldProps = CommonFieldProps &
  Omit<SelectHTMLAttributes<HTMLSelectElement>, 'className'> & { children: ReactNode };

/** Native select (best mobile behaviour and accessibility) styled like the design (419). */
export function SelectField({
  label,
  hideLabel,
  optional,
  helper,
  error,
  className,
  children,
  id,
  ...select
}: SelectFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <Field
      id={fieldId}
      label={label}
      hideLabel={hideLabel}
      optional={optional}
      helper={helper}
      error={error}
      className={className}
    >
      {({ invalid, describedBy }) => (
        <div className="relative">
          <select
            id={fieldId}
            aria-invalid={invalid || undefined}
            aria-describedby={describedBy}
            className={cn(controlClasses({ invalid }), 'appearance-none pe-10')}
            {...select}
          >
            {children}
          </select>
          <Icon
            name="chevD"
            className="pointer-events-none absolute end-3 top-1/2 size-[18px] -translate-y-1/2 text-text-tertiary"
          />
        </div>
      )}
    </Field>
  );
}

type TextareaFieldProps = CommonFieldProps & Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'className'>;

export function TextareaField({
  label,
  hideLabel,
  optional,
  helper,
  error,
  className,
  id,
  ...textarea
}: TextareaFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <Field
      id={fieldId}
      label={label}
      hideLabel={hideLabel}
      optional={optional}
      helper={helper}
      error={error}
      className={className}
    >
      {({ invalid, describedBy }) => (
        <textarea
          id={fieldId}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          className={cn(controlClasses({ invalid }), 'min-h-[76px] py-3 leading-[1.8]')}
          {...textarea}
        />
      )}
    </Field>
  );
}

type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'className'> & {
  label: ReactNode;
  description?: ReactNode;
  className?: string;
};

export function Checkbox({ label, description, className, id, ...input }: CheckboxProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <div className={cn('flex items-start gap-3', className)}>
      <input
        id={fieldId}
        type="checkbox"
        className="mt-1 size-5 shrink-0 cursor-pointer rounded-xs accent-navy-900"
        aria-describedby={description ? `${fieldId}-description` : undefined}
        {...input}
      />
      <div className="flex flex-col">
        <label htmlFor={fieldId} className="cursor-pointer text-caption text-text-primary">
          {label}
        </label>
        {description && (
          <span id={`${fieldId}-description`} className="text-helper text-text-tertiary">
            {description}
          </span>
        )}
      </div>
    </div>
  );
}

type SwitchProps = {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  label: ReactNode;
  /** Hide the visible label (e.g. inside a table row that already names it). */
  hideLabel?: boolean;
  disabled?: boolean;
  className?: string;
};

/**
 * Toggle switch (design 3689–3697): 38×22 track, success green when on. The knob moves to the
 * inline end, so it mirrors in Arabic exactly as in the design. The off track uses a 3:1 token (D-049).
 */
export function Switch({ checked, onCheckedChange, label, hideLabel, disabled, className }: SwitchProps) {
  const id = useId();
  return (
    <div className={cn('inline-flex items-center gap-2', className)}>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onCheckedChange(!checked)}
        className="flex min-h-11 min-w-11 shrink-0 items-center justify-center rounded-field disabled:cursor-not-allowed disabled:opacity-60"
      >
        <span
          aria-hidden="true"
          className={cn(
            'flex h-[22px] w-[38px] items-center rounded-pill p-0.5 transition-colors',
            checked ? 'justify-end bg-success-500' : 'justify-start bg-switch-off',
          )}
        >
          <span className="size-[18px] rounded-full bg-surface shadow-[0_1px_2px_rgb(16_40_61/.25)]" />
        </span>
      </button>
      <label
        htmlFor={id}
        className={cn('cursor-pointer text-caption text-text-primary', hideLabel && 'sr-only')}
      >
        {label}
      </label>
    </div>
  );
}
