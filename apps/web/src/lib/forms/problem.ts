import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import type { ApiError } from '@/lib/api/problem';
import { VALIDATION_KEYS } from './validation';

/**
 * Maps an API error code (e.g. "name.required", "booking.phone_incomplete", FluentValidation's
 * "NotEmptyValidator") to a validation message key. Unknown codes become "generic".
 */
export function codeToMessageKey(code: string | undefined): string {
  if (!code) return 'generic';
  if (VALIDATION_KEYS.has(code)) return code;
  const last = code.split('.').pop() ?? code;
  const camel = last.replace(/[_-]([a-z])/g, (_, c: string) => c.toUpperCase());
  if (VALIDATION_KEYS.has(camel)) return camel;
  if (/^NotEmpty|^NotNull/.test(code)) return 'required';
  return 'generic';
}

/**
 * Applies a `validation.failed` problem response (R-WEB-14) to a react-hook-form instance:
 * known fields get their message key; errors for fields the form does not render (or a
 * non-validation failure) become a form-level `root.server` error. Returns true when handled.
 */
export function applyProblemToForm<T extends FieldValues>(
  error: ApiError,
  setError: UseFormSetError<T>,
  fields: ReadonlyArray<Path<T>>,
): boolean {
  if (!error.isValidation) {
    setError('root.server' as Path<T>, { type: 'server', message: error.errorCode });
    return false;
  }

  const known = new Set<string>(fields);
  let unknownFieldFailed = false;

  for (const [field, codes] of Object.entries(error.fieldErrors)) {
    if (known.has(field)) {
      setError(
        field as Path<T>,
        { type: 'server', message: codeToMessageKey(codes[0]) },
        { shouldFocus: true },
      );
    } else {
      unknownFieldFailed = true;
    }
  }

  if (unknownFieldFailed || Object.keys(error.fieldErrors).length === 0) {
    setError('root.server' as Path<T>, { type: 'server', message: 'form' });
  }
  return true;
}
