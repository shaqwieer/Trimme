import { z } from 'zod';
import ar from '../../../messages/ar.json';
import { digitsOnly, isValidSaudiMobile, normalizeSaudiMobile } from '@/components/ui/digits';

/**
 * Validation messages are *keys* into the `validation` message namespace, never user-facing text,
 * so every error renders in the active locale (spec §6). Parameters use "key|name=value" form,
 * e.g. "tooLong|max=120".
 */
export type ValidationKey = keyof typeof ar.validation;

export const VALIDATION_KEYS: ReadonlySet<string> = new Set(Object.keys(ar.validation));

export function withParams(key: ValidationKey, params: Record<string, string | number>): string {
  return [key, ...Object.entries(params).map(([name, value]) => `${name}=${value}`)].join('|');
}

/** Splits an encoded message into its key and parameters; unknown keys fall back to "generic". */
export function parseValidationMessage(message: string | undefined): {
  key: ValidationKey;
  params: Record<string, string>;
} {
  const [rawKey = 'generic', ...rest] = (message ?? 'generic').split('|');
  const params = Object.fromEntries(rest.map((pair) => pair.split('=') as [string, string]));
  const key = (VALIDATION_KEYS.has(rawKey) ? rawKey : 'generic') as ValidationKey;
  return { key, params };
}

/** Required, trimmed text with an optional maximum length. */
export function requiredText(max?: number) {
  const base = z.string().trim().min(1, { error: 'required' });
  return max ? base.max(max, { error: withParams('tooLong', { max }) }) : base;
}

/** Saudi mobile as typed (national digits or +966/05 forms) → normalised national digits. */
export const saudiMobile = z
  .string()
  .transform((value) => normalizeSaudiMobile(value))
  .superRefine((national, ctx) => {
    if (national.length === 0) ctx.addIssue({ code: 'custom', message: 'required' });
    else if (national.length < 9) ctx.addIssue({ code: 'custom', message: 'phoneIncomplete' });
    else if (!isValidSaudiMobile(national)) ctx.addIssue({ code: 'custom', message: 'phoneInvalid' });
  });

/** One-time code with exactly `length` digits (D-037: 6). */
export function otpCode(length = 6) {
  return z
    .string()
    .transform((value) => digitsOnly(value, length))
    .refine((code) => code.length === length, { error: 'otpIncomplete' });
}

export const email = z
  .string()
  .trim()
  .min(1, { error: 'required' })
  .pipe(z.email({ error: 'emailInvalid' }));
