'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { z } from 'zod';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { withReturnTo } from '@/lib/auth/paths';
import { FormPhoneField, useValidationMessage, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm } from '@/lib/forms/problem';
import { saudiMobile } from '@/lib/forms/validation';
import { toE164 } from '@/components/ui/digits';
import { AuthCard } from './AuthCard';
import { useOtpFlow } from './OtpFlow';

type Mode = 'signUp' | 'signIn';

const schema = (mode: Mode) =>
  z.object({
    phone: saudiMobile,
    termsAccepted: z
      .boolean()
      .refine((accepted) => mode === 'signIn' || accepted, { error: 'termsRequired' }),
  });

/**
 * Customer sign-up / sign-in, step 1 (design c-auth "SIGN UP · OTP REQUEST"): mobile number with the fixed +966
 * prefix; sign-up also asks for terms acceptance. Both send the same OTP (D-005); the verify step tells new and
 * returning customers apart.
 */
export function PhoneStep({ mode, returnTo }: { mode: Mode; returnTo?: string }) {
  const t = useTranslations('auth');
  const locale = useLocale();
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const validationMessage = useValidationMessage();
  const { setChallenge } = useOtpFlow();
  const [serverError, setServerError] = useState<string>();
  const form = useZodForm(schema(mode), {
    mode: 'onSubmit',
    defaultValues: { phone: '', termsAccepted: mode === 'signIn' },
  });

  const onSubmit = form.handleSubmit(async ({ phone, termsAccepted }) => {
    setServerError(undefined);
    const phoneE164 = toE164(phone) ?? '';
    try {
      const challenge = ensureOk(
        await browserApi.POST('/api/v1/auth/otp/request', {
          body: { phone: phoneE164, termsAccepted, locale },
        }),
      );
      setChallenge({ ...challenge, phoneE164, termsAccepted });
      router.push(withReturnTo('/auth/verify', returnTo));
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        applyProblemToForm(error, form.setError, ['phone']);
      } else {
        setServerError(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      }
    }
  });

  const other = mode === 'signUp' ? '/auth/sign-in' : '/auth/sign-up';

  return (
    <AuthCard
      showLogo
      title={t(`${mode}.title`)}
      subtitle={t(`${mode}.subtitle`)}
      footer={
        <div className="flex flex-col items-center gap-3">
          <p>
            {mode === 'signUp' ? t('signUp.haveAccount') : t('signIn.noAccount')}{' '}
            <Link href={withReturnTo(other, returnTo)} className="font-bold text-brand-700 hover:underline">
              {mode === 'signUp' ? t('signUp.signIn') : t('signIn.signUp')}
            </Link>
          </p>
          {mode === 'signIn' && (
            <Link href="/auth/staff/sign-in" className="text-helper text-text-tertiary hover:underline">
              {t('signIn.staff')}
            </Link>
          )}
        </div>
      }
    >
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <FormPhoneField control={form.control} name="phone" label={t('phoneLabel')} autoFocus required />
        {mode === 'signUp' && (
          <div className="flex flex-col gap-1">
            <Checkbox
              {...form.register('termsAccepted')}
              label={t.rich('signUp.terms', {
                strong: (chunks) => <strong className="font-bold">{chunks}</strong>,
              })}
              aria-invalid={form.formState.errors.termsAccepted ? true : undefined}
            />
            {form.formState.errors.termsAccepted && (
              <p role="alert" className="text-helper text-danger-700">
                {validationMessage(form.formState.errors.termsAccepted)}
              </p>
            )}
          </div>
        )}
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t(`${mode}.submit`)}
        </Button>
      </form>
    </AuthCard>
  );
}
