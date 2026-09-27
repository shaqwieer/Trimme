'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Controller, useFieldArray } from 'react-hook-form';
import { z } from 'zod';
import { Button, IconButton } from '@/components/ui/Button';
import { SelectField, Switch, TextField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { FormTextareaField, FormTextField, useValidationMessage, useZodForm } from '@/lib/forms/fields';
import { parsePrice } from '@/lib/forms/price';
import { applyProblemToForm } from '@/lib/forms/problem';
import { requiredText, withParams } from '@/lib/forms/validation';

type Plan = components['schemas']['PlanResponse'];

const MAX_FEATURES = 12;
const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, { error: withParams('tooLong', { max }) });
/** Empty, or a whole number in range (typed digits may be Arabic-Indic; the input is numeric). */
const optionalWhole = (min: number, max: number) =>
  z
    .string()
    .refine(
      (value) =>
        value.trim() === '' ||
        (Number.isInteger(Number(value)) && Number(value) >= min && Number(value) <= max),
      {
        error: 'outOfRange',
      },
    );

export const planSchema = z
  .object({
    nameAr: requiredText(80),
    nameEn: requiredText(80),
    descriptionAr: optionalText(500),
    descriptionEn: optionalText(500),
    features: z
      .array(z.object({ ar: requiredText(120), en: requiredText(120) }))
      .max(MAX_FEATURES, { error: 'tooMany' }),
    intervalUnit: z.enum(['Month', 'Day']),
    intervalCount: z.string(),
    maxProfessionals: optionalWhole(1, 10_000),
    maxServices: optionalWhole(1, 10_000),
    trialDays: optionalWhole(0, 365),
    graceDays: optionalWhole(0, 365),
    availableToNewShops: z.boolean(),
    initialPrice: z
      .string()
      .refine((value) => value.trim() === '' || parsePrice(value) !== null, { error: 'priceInvalid' }),
  })
  .superRefine((values, ctx) => {
    const count = Number(values.intervalCount);
    const max = values.intervalUnit === 'Month' ? 36 : 1095;
    if (!Number.isInteger(count) || count < 1 || count > max)
      ctx.addIssue({ code: 'custom', path: ['intervalCount'], message: 'outOfRange' });
  });

const FIELDS = [
  'nameAr',
  'nameEn',
  'descriptionAr',
  'descriptionEn',
  'features',
  'intervalCount',
  'maxProfessionals',
  'maxServices',
  'trialDays',
  'graceDays',
  'initialPrice',
] as const;

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());
const wholeOrNull = (value: string) => (value.trim() === '' ? null : Number(value));

/**
 * SuperAdmin plan editor (DV-A10): localized names and description, feature lines in both languages, the billing
 * interval, optional limits and trial/grace days, and whether new shops can be put on it. Prices are never edited
 * here: a new price is a new version (see the price history on the plan page). On create, an optional first price.
 */
export function PlanForm({ plan }: { plan?: Plan }) {
  const t = useTranslations('subscriptionPlans.form');
  const router = useRouter();
  const message = useValidationMessage();
  const apiMessage = useApiErrorMessage();
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);

  const form = useZodForm(planSchema, {
    mode: 'onSubmit',
    defaultValues: {
      nameAr: plan?.nameAr ?? '',
      nameEn: plan?.nameEn ?? '',
      descriptionAr: plan?.descriptionAr ?? '',
      descriptionEn: plan?.descriptionEn ?? '',
      features: plan?.features.map((f) => ({ ar: f.ar, en: f.en })) ?? [],
      intervalUnit: plan?.intervalUnit ?? 'Month',
      intervalCount: String(plan?.intervalCount ?? 1),
      maxProfessionals: plan?.maxProfessionals != null ? String(plan.maxProfessionals) : '',
      maxServices: plan?.maxServices != null ? String(plan.maxServices) : '',
      trialDays: plan?.trialDays != null ? String(plan.trialDays) : '',
      graceDays: plan?.graceDays != null ? String(plan.graceDays) : '',
      availableToNewShops: plan?.availableToNewShops ?? true,
      initialPrice: '',
    },
  });
  const features = useFieldArray({ control: form.control, name: 'features' });
  const archived = plan?.status === 'Archived';

  const onSubmit = form.handleSubmit(async (values) => {
    setFailure(undefined);
    setSaved(false);
    const details = {
      nameAr: values.nameAr,
      nameEn: values.nameEn,
      descriptionAr: orNull(values.descriptionAr),
      descriptionEn: orNull(values.descriptionEn),
      features: values.features.map((f) => ({ ar: f.ar.trim(), en: f.en.trim() })),
      maxProfessionals: wholeOrNull(values.maxProfessionals),
      maxServices: wholeOrNull(values.maxServices),
      intervalUnit: values.intervalUnit,
      intervalCount: Number(values.intervalCount),
      trialDays: wholeOrNull(values.trialDays),
      graceDays: wholeOrNull(values.graceDays),
      availableToNewShops: values.availableToNewShops,
    };
    try {
      if (plan) {
        ensureOk(
          await browserApi.PUT('/api/v1/admin/subscription-plans/{planId}', {
            params: { path: { planId: plan.id } },
            body: { ...details, version: plan.version },
          }),
        );
        setSaved(true);
        router.refresh();
      } else {
        const created = ensureOk(
          await browserApi.POST('/api/v1/admin/subscription-plans', {
            body: {
              ...details,
              initialPrice: values.initialPrice.trim() === '' ? null : parsePrice(values.initialPrice),
            },
          }),
        );
        router.push(`/admin/subscription-plans/${created.id}`);
      }
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) applyProblemToForm(error, form.setError, FIELDS);
      else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    }
  });

  const rootError = form.formState.errors.root?.server?.message;
  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5" data-testid="plan-form">
      <fieldset disabled={archived} className="grid gap-4 md:grid-cols-2">
        <FormTextField control={form.control} name="nameAr" label={t('nameAr')} dir="rtl" />
        <FormTextField control={form.control} name="nameEn" label={t('nameEn')} dir="ltr" />
        <FormTextareaField
          control={form.control}
          name="descriptionAr"
          label={t('descriptionAr')}
          dir="rtl"
          optional
          maxLength={500}
        />
        <FormTextareaField
          control={form.control}
          name="descriptionEn"
          label={t('descriptionEn')}
          dir="ltr"
          optional
          maxLength={500}
        />
        <Controller
          control={form.control}
          name="intervalUnit"
          render={({ field }) => (
            <SelectField
              label={t('intervalUnit')}
              name={field.name}
              value={field.value}
              onChange={field.onChange}
            >
              <option value="Month">{t('units.Month')}</option>
              <option value="Day">{t('units.Day')}</option>
            </SelectField>
          )}
        />
        <FormTextField
          control={form.control}
          name="intervalCount"
          label={t('intervalCount')}
          dir="ltr"
          inputMode="numeric"
          autoComplete="off"
        />
        <FormTextField
          control={form.control}
          name="maxProfessionals"
          label={t('maxProfessionals')}
          helper={t('limitHelper')}
          dir="ltr"
          inputMode="numeric"
          optional
        />
        <FormTextField
          control={form.control}
          name="maxServices"
          label={t('maxServices')}
          helper={t('limitHelper')}
          dir="ltr"
          inputMode="numeric"
          optional
        />
        <FormTextField
          control={form.control}
          name="trialDays"
          label={t('trialDays')}
          helper={t('trialGraceHelper')}
          dir="ltr"
          inputMode="numeric"
          optional
        />
        <FormTextField
          control={form.control}
          name="graceDays"
          label={t('graceDays')}
          helper={t('trialGraceHelper')}
          dir="ltr"
          inputMode="numeric"
          optional
        />
        {!plan && (
          <FormTextField
            control={form.control}
            name="initialPrice"
            label={t('initialPrice')}
            helper={t('initialPriceHelper')}
            dir="ltr"
            inputMode="decimal"
            autoComplete="off"
            optional
          />
        )}
        <Controller
          control={form.control}
          name="availableToNewShops"
          render={({ field }) => (
            <div className="flex items-center gap-3 self-end pb-2">
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                label={t('availableToNewShops')}
              />
            </div>
          )}
        />
      </fieldset>

      <fieldset disabled={archived} className="flex flex-col gap-3">
        <legend className="text-label font-bold text-text-primary">{t('features')}</legend>
        <p className="text-helper text-text-tertiary">{t('featuresHelper')}</p>
        {features.fields.map((feature, index) => (
          <div
            key={feature.id}
            className="flex flex-col gap-2 rounded-button border border-border p-3 md:flex-row md:items-start"
          >
            <Controller
              control={form.control}
              name={`features.${index}.ar`}
              render={({ field, fieldState }) => (
                <TextField
                  className="flex-1"
                  label={t('featureAr', { n: index + 1 })}
                  dir="rtl"
                  {...field}
                  error={message(fieldState.error)}
                />
              )}
            />
            <Controller
              control={form.control}
              name={`features.${index}.en`}
              render={({ field, fieldState }) => (
                <TextField
                  className="flex-1"
                  label={t('featureEn', { n: index + 1 })}
                  dir="ltr"
                  {...field}
                  error={message(fieldState.error)}
                />
              )}
            />
            <IconButton
              icon="trash"
              label={t('removeFeature', { n: index + 1 })}
              className="md:mt-7"
              onClick={() => features.remove(index)}
            />
          </div>
        ))}
        {features.fields.length < MAX_FEATURES && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            icon="plus"
            className="self-start"
            onClick={() => features.append({ ar: '', en: '' })}
          >
            {t('addFeature')}
          </Button>
        )}
      </fieldset>

      {(failure || rootError) && (
        <InlineAlert tone="danger" title={failure ?? message({ message: rootError })} />
      )}
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {!archived && (
        <Button type="submit" size="md" loading={form.formState.isSubmitting} className="self-start">
          {plan ? t('save') : t('create')}
        </Button>
      )}
    </form>
  );
}
