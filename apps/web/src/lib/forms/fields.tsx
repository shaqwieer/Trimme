'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslations } from 'next-intl';
import type { ComponentProps } from 'react';
import {
  type Control,
  Controller,
  type FieldError,
  type FieldValues,
  type Path,
  type Resolver,
  useForm,
  type UseFormProps,
} from 'react-hook-form';
import type { z } from 'zod';
import { PhoneField, TextareaField, TextField } from '@/components/ui/inputs';
import { parseValidationMessage } from './validation';

/** react-hook-form wired to a Zod schema (validation on blur, then on change). */
export function useZodForm<Schema extends z.ZodType<FieldValues, FieldValues>>(
  schema: Schema,
  options: Omit<UseFormProps<z.input<Schema>, unknown, z.output<Schema>>, 'resolver'> = {},
) {
  return useForm<z.input<Schema>, unknown, z.output<Schema>>({
    mode: 'onTouched',
    ...options,
    // zodResolver's overloads cannot infer through a generic schema parameter; the types are equivalent.
    resolver: zodResolver(schema as never) as unknown as Resolver<z.input<Schema>, unknown, z.output<Schema>>,
  });
}

/** Translates an encoded validation message key (see validation.ts) into the active locale. */
export function useValidationMessage() {
  const t = useTranslations('validation');
  return (error: Pick<FieldError, 'message'> | undefined): string | undefined => {
    if (!error) return undefined;
    const { key, params } = parseValidationMessage(error.message);
    return t(key, params);
  };
}

type Bound<T extends FieldValues> = { control: Control<T>; name: Path<T> };

export function FormTextField<T extends FieldValues>({
  control,
  name,
  ...props
}: Bound<T> & Omit<ComponentProps<typeof TextField>, 'name' | 'value' | 'onChange' | 'onBlur' | 'error'>) {
  const message = useValidationMessage();
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <TextField
          {...props}
          name={field.name}
          ref={field.ref}
          value={field.value ?? ''}
          onChange={field.onChange}
          onBlur={field.onBlur}
          error={message(fieldState.error)}
        />
      )}
    />
  );
}

export function FormTextareaField<T extends FieldValues>({
  control,
  name,
  ...props
}: Bound<T> &
  Omit<ComponentProps<typeof TextareaField>, 'name' | 'value' | 'onChange' | 'onBlur' | 'error'>) {
  const message = useValidationMessage();
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <TextareaField
          {...props}
          name={field.name}
          value={field.value ?? ''}
          onChange={field.onChange}
          onBlur={field.onBlur}
          error={message(fieldState.error)}
        />
      )}
    />
  );
}

export function FormPhoneField<T extends FieldValues>({
  control,
  name,
  ...props
}: Bound<T> &
  Omit<ComponentProps<typeof PhoneField>, 'name' | 'value' | 'onValueChange' | 'onBlur' | 'error'>) {
  const message = useValidationMessage();
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <PhoneField
          {...props}
          name={field.name}
          value={field.value ?? ''}
          onValueChange={(national) => field.onChange(national)}
          onBlur={field.onBlur}
          error={message(fieldState.error)}
        />
      )}
    />
  );
}
