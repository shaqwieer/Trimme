'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import * as z from 'zod';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/inputs';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { asLocale, LANGUAGE_NAME_KEY, routing } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { FormTextField, useValidationMessage, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm } from '@/lib/forms/problem';
import { requiredText } from '@/lib/forms/validation';
import { AuthCard } from './AuthCard';

const schema = z.object({
  displayName: requiredText(60),
  preferredLocale: z.enum(routing.locales),
  termsAccepted: z.boolean().refine((accepted) => accepted, { error: 'termsRequired' }),
});

/**
 * New customers give a name, language and terms acceptance after their first verification (the design shows the
 * name in the profile and reviews but does not draw this step — DV-A01).
 */
export function CompleteProfileForm({ returnTo, initialName }: { returnTo: string; initialName?: string }) {
  const t = useTranslations('auth.completeProfile');
  const locale = useLocale();
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const validationMessage = useValidationMessage();
  const [serverError, setServerError] = useState<string>();
  const form = useZodForm(schema, {
    defaultValues: {
      displayName: initialName ?? '',
      preferredLocale: asLocale(locale),
      termsAccepted: false,
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    try {
      ensureOk(await browserApi.POST('/api/v1/auth/profile/complete', { body: values }));
      router.replace(returnTo, { locale: values.preferredLocale });
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        applyProblemToForm(error, form.setError, ['displayName', 'preferredLocale', 'termsAccepted']);
      } else {
        setServerError(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      }
    }
  });

  return (
    <AuthCard title={t('title')} subtitle={t('subtitle')}>
      <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <FormTextField
          control={form.control}
          name="displayName"
          label={t('nameLabel')}
          autoComplete="name"
          autoFocus
        />
        <fieldset className="flex min-w-0 flex-col gap-2">
          <legend className="pb-2 text-label font-bold text-text-strong">{t('languageLabel')}</legend>
          {/* One per row: three names side by side do not fit the narrow sign-in card. */}
          <div className="grid gap-3">
            {routing.locales.map((option) => (
              <RadioCard key={option} {...form.register('preferredLocale')} value={option}>
                <span lang={option}>{t(LANGUAGE_NAME_KEY[option])}</span>
              </RadioCard>
            ))}
          </div>
        </fieldset>
        <div className="flex flex-col gap-1">
          <Checkbox
            {...form.register('termsAccepted')}
            label={t.rich('terms', { strong: (chunks) => <strong className="font-bold">{chunks}</strong> })}
            aria-invalid={form.formState.errors.termsAccepted ? true : undefined}
          />
          {form.formState.errors.termsAccepted && (
            <p role="alert" className="text-helper text-danger-700">
              {validationMessage(form.formState.errors.termsAccepted)}
            </p>
          )}
        </div>
        {serverError && <InlineAlert tone="danger" title={serverError} />}
        <Button type="submit" fullWidth loading={form.formState.isSubmitting}>
          {t('submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
