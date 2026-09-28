'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { codeToMessageKey } from '@/lib/forms/problem';

/**
 * One message for a schedule form: a validation problem shows its first field's message, any other API error its
 * localized code, and a plain string is shown as is.
 */
export function useScheduleErrors() {
  const tv = useTranslations('validation');
  const apiMessage = useApiErrorMessage();
  const [failure, setFailure] = useState<string>();
  return {
    failure,
    clear: () => setFailure(undefined),
    fail: (error: unknown) => {
      if (typeof error === 'string') setFailure(error);
      else if (error instanceof ApiError && error.isValidation) {
        const code = Object.values(error.fieldErrors)[0]?.[0];
        setFailure(tv(codeToMessageKey(code) as 'generic', { max: 200 }));
      } else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    },
  };
}
