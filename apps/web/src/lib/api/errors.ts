'use client';

import { useTranslations } from 'next-intl';
import { ApiError, isProblemDetails } from './problem';

/** `otp.resend_cooldown` → `otp_resend_cooldown` (next-intl treats dots as nesting). */
export function apiErrorKey(code: string): string {
  return code.replace(/\./g, '_');
}

/**
 * Localizes an API error code (spec §6: clients localize from stable codes). `retryAfterSeconds` is exposed to the
 * messages as `seconds` and rounded-up `minutes`. Unknown codes fall back to a generic message.
 */
export function useApiErrorMessage() {
  const typed = useTranslations('apiErrors');
  // Codes come from the API at runtime, so the key is a plain string here; `has` guards unknown codes.
  const t = typed as unknown as {
    (key: string, values?: Record<string, number>): string;
    has(key: string): boolean;
  };
  return (error: ApiError | string | undefined): string | undefined => {
    if (!error) return undefined;
    const code = typeof error === 'string' ? error : error.errorCode;
    const key = apiErrorKey(code);
    if (!t.has(key)) return t('generic');
    const retryAfter = typeof error === 'string' ? undefined : error.details.retryAfterSeconds;
    const seconds = retryAfter ?? 0;
    return t(key, { seconds, minutes: Math.max(1, Math.ceil(seconds / 60)) });
  };
}

/**
 * Returns the data of a successful openapi-fetch result, or throws an {@link ApiError} built from the problem body
 * (openapi-fetch has already parsed it), so forms handle every failure the same way.
 */
export function ensureOk<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.response.ok) return result.data as T;
  throw new ApiError(result.response.status, isProblemDetails(result.error) ? result.error : undefined);
}
