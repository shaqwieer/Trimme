'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import * as z from 'zod';
import { Ltr } from '@/components/text/Ltr';
import { Button } from '@/components/ui/Button';
import { toE164 } from '@/components/ui/digits';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { LANGUAGE_NAME_KEY, type Locale, routing } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { FormPhoneField, FormTextField, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm } from '@/lib/forms/problem';
import { requiredText, saudiMobile } from '@/lib/forms/validation';

const schema = z.object({
  displayName: requiredText(60),
  email: z
    .string()
    .trim()
    .max(254, { error: 'tooLong' })
    .refine((value) => value === '' || z.email().safeParse(value).success, { error: 'emailInvalid' }),
  phone: z.string(),
  preferredLocale: z.enum(routing.locales),
});

export type ProfileFormValues = {
  displayName: string;
  email: string | null;
  phoneMasked: string | null;
  phoneChangeAllowed: boolean;
  preferredLocale: Locale;
};

/**
 * Personal info (c-profile "البيانات الشخصية", D-144): the name shops see on bookings, an optional email for the
 * customer's own records, the mobile number they sign in with, and the language of the interface and messages. The
 * number shows masked; changing it asks for the new one and warns that it becomes the sign-in number. While sign-in
 * codes are on the number cannot change here (the API refuses it too).
 */
export function ProfileForm({ profile }: { profile: ProfileFormValues }) {
  const t = useTranslations('account.profile');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [serverError, setServerError] = useState<string>();
  const [changingPhone, setChangingPhone] = useState(false);
  const form = useZodForm(schema, {
    defaultValues: {
      displayName: profile.displayName,
      email: profile.email ?? '',
      phone: '',
      preferredLocale: profile.preferredLocale,
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(undefined);
    let phone: string | null = null;
    if (changingPhone) {
      const parsed = saudiMobile.safeParse(values.phone);
      if (!parsed.success) {
        form.setError('phone', { message: parsed.error.issues[0]?.message ?? 'phoneInvalid' });
        return;
      }
      phone = toE164(parsed.data) ?? null;
    }
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/me/profile', {
          body: {
            displayName: values.displayName,
            preferredLocale: values.preferredLocale,
            email: values.email || null,
            phone,
          },
        }),
      );
      router.replace('/account?saved=1', { locale: values.preferredLocale });
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        applyProblemToForm(error, form.setError, ['displayName', 'email', 'phone', 'preferredLocale']);
      } else {
        setServerError(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      }
    }
  });

  return (
    <form
      method="post"
      noValidate
      onSubmit={onSubmit}
      className="flex flex-col gap-5"
      data-testid="profile-form"
    >
      <FormTextField control={form.control} name="displayName" label={t('nameLabel')} autoComplete="name" />
      <FormTextField
        control={form.control}
        name="email"
        type="email"
        dir="ltr"
        label={t('emailLabel')}
        helper={t('emailHint')}
        optional
        autoComplete="email"
        inputMode="email"
      />

      <fieldset className="flex min-w-0 flex-col gap-2" data-testid="profile-phone">
        <legend className="pb-2 text-label font-bold text-text-strong">{t('phoneLabel')}</legend>
        <div className="flex flex-wrap items-center gap-3 rounded-field border border-border bg-bg-subtle px-4 py-3">
          <span className="font-latin text-label font-bold text-text-primary">
            <Ltr>{profile.phoneMasked ?? '—'}</Ltr>
          </span>
          {profile.phoneChangeAllowed && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="ms-auto"
              onClick={() => {
                if (changingPhone) form.setValue('phone', '');
                form.clearErrors('phone');
                setChangingPhone(!changingPhone);
              }}
            >
              {changingPhone ? t('phoneCancel') : t('phoneChange')}
            </Button>
          )}
        </div>
        {!profile.phoneChangeAllowed && <p className="text-helper text-text-secondary">{t('phoneLocked')}</p>}
        {changingPhone && (
          <div className="flex flex-col gap-3">
            <FormPhoneField control={form.control} name="phone" label={t('phoneNewLabel')} />
            <InlineAlert tone="warning" title={t('phoneWarningTitle')}>
              {t('phoneWarningBody')}
            </InlineAlert>
          </div>
        )}
      </fieldset>

      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="pb-2 text-label font-bold text-text-strong">{t('languageLabel')}</legend>
        <div className="grid gap-3 sm:grid-cols-3">
          {routing.locales.map((option) => (
            <RadioCard key={option} {...form.register('preferredLocale')} value={option}>
              <span lang={option}>{t(LANGUAGE_NAME_KEY[option])}</span>
            </RadioCard>
          ))}
        </div>
      </fieldset>
      {serverError && <InlineAlert tone="danger" title={serverError} />}
      <Button type="submit" loading={form.formState.isSubmitting} className="self-start">
        {t('save')}
      </Button>
    </form>
  );
}
