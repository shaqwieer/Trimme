'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { z } from 'zod';
import { Button } from '@/components/ui/Button';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { FormTextField, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm } from '@/lib/forms/problem';
import { requiredText } from '@/lib/forms/validation';

const schema = z.object({
  displayName: requiredText(60),
  preferredLocale: z.enum(['ar', 'en']),
});

/**
 * Personal info (c-profile "البيانات الشخصية"): the name shops see on bookings and the language of the interface and
 * messages. It reuses the profile command; the terms the customer accepted at sign-up stay as they were.
 */
export function ProfileForm({
  displayName,
  preferredLocale,
}: {
  displayName: string;
  preferredLocale: 'ar' | 'en';
}) {
  const t = useTranslations('account.profile');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [serverError, setServerError] = useState<string>();
  const form = useZodForm(schema, { defaultValues: { displayName, preferredLocale } });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/auth/profile/complete', { body: { ...values, termsAccepted: true } }),
      );
      router.replace('/account?saved=1', { locale: values.preferredLocale });
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        applyProblemToForm(error, form.setError, ['displayName', 'preferredLocale']);
      } else {
        setServerError(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      }
    }
  });

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5" data-testid="profile-form">
      <FormTextField control={form.control} name="displayName" label={t('nameLabel')} autoComplete="name" />
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="pb-2 text-label font-bold text-text-strong">{t('languageLabel')}</legend>
        <div className="grid grid-cols-2 gap-3">
          <RadioCard {...form.register('preferredLocale')} value="ar">
            {t('arabic')}
          </RadioCard>
          <RadioCard {...form.register('preferredLocale')} value="en">
            {t('english')}
          </RadioCard>
        </div>
      </fieldset>
      {serverError && <InlineAlert tone="danger" title={serverError} />}
      <Button type="submit" loading={form.formState.isSubmitting} className="self-start">
        {t('save')}
      </Button>
    </form>
  );
}
