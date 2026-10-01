'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Controller } from 'react-hook-form';
import * as z from 'zod';
import { Button } from '@/components/ui/Button';
import { Checkbox, SelectField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { FormTextareaField, FormTextField, useValidationMessage, useZodForm } from '@/lib/forms/fields';
import { DURATION_OPTIONS, parsePrice } from '@/lib/forms/price';
import { applyProblemToForm } from '@/lib/forms/problem';
import { requiredText, withParams } from '@/lib/forms/validation';
import { formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Service = components['schemas']['ShopServiceResponse'];
type Package = components['schemas']['ShopPackageResponse'];

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, { error: withParams('tooLong', { max }) });

const schema = z.object({
  nameAr: requiredText(120),
  nameEn: optionalText(120),
  descriptionAr: optionalText(500),
  descriptionEn: optionalText(500),
  price: z.string().refine((value) => parsePrice(value) !== null, { error: 'priceInvalid' }),
  durationMinutes: z
    .string()
    .refine((value) => DURATION_OPTIONS.includes(Number(value)), { error: 'durationInvalid' }),
  serviceIds: z
    .array(z.string())
    .refine((ids) => ids.length >= 2 && ids.length <= 10, { error: 'packageItems' }),
});

const FIELDS = [
  'nameAr',
  'nameEn',
  'descriptionAr',
  'descriptionEn',
  'price',
  'durationMinutes',
  'serviceIds',
] as const;
const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

/** A package of the shop's own services (D-020): own price and total duration; items kept in the list's order. */
export function PackageForm({ services, pkg }: { services: Service[]; pkg?: Package }) {
  const t = useTranslations('shopServices.form');
  const tServices = useTranslations('shopServices');
  const locale = useLocale();
  const lang = locale === 'en' ? 'en' : 'ar';
  const router = useRouter();
  const message = useValidationMessage();
  const apiMessage = useApiErrorMessage();
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);
  const choices = services.filter((s) => !s.isArchived);

  const form = useZodForm(schema, {
    mode: 'onSubmit',
    defaultValues: {
      nameAr: pkg?.nameAr ?? '',
      nameEn: pkg?.nameEn ?? '',
      descriptionAr: pkg?.descriptionAr ?? '',
      descriptionEn: pkg?.descriptionEn ?? '',
      price: pkg ? String(pkg.price) : '',
      durationMinutes: String(pkg?.durationMinutes ?? 60),
      serviceIds: pkg?.items.map((item) => item.serviceId) ?? [],
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setFailure(undefined);
    setSaved(false);
    // Items follow the service list's display order.
    const serviceIds = choices.map((s) => s.id).filter((id) => values.serviceIds.includes(id));
    const body = {
      nameAr: values.nameAr,
      nameEn: orNull(values.nameEn),
      descriptionAr: orNull(values.descriptionAr),
      descriptionEn: orNull(values.descriptionEn),
      price: parsePrice(values.price) ?? 0,
      durationMinutes: Number(values.durationMinutes),
      serviceIds,
    };
    try {
      if (pkg) {
        ensureOk(
          await browserApi.PUT('/api/v1/shop/packages/{packageId}', {
            params: { path: { packageId: pkg.id } },
            body: { ...body, version: pkg.version },
          }),
        );
        setSaved(true);
        router.refresh();
      } else {
        ensureOk(await browserApi.POST('/api/v1/shop/packages', { body }));
        router.push('/shop/services?tab=packages');
      }
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, [...FIELDS]);
      else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    }
  });

  return (
    <form
      method="post"
      noValidate
      onSubmit={onSubmit}
      className="flex flex-col gap-5"
      data-testid="package-form"
    >
      <div className="grid gap-4 md:grid-cols-2">
        <FormTextField control={form.control} name="nameAr" label={t('nameAr')} dir="rtl" />
        <FormTextField
          control={form.control}
          name="nameEn"
          label={t('nameEn')}
          helper={t('nameEnHelper')}
          dir="ltr"
          optional
        />
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
        <FormTextField
          control={form.control}
          name="price"
          label={t('price')}
          dir="ltr"
          inputMode="decimal"
          autoComplete="off"
        />
        <Controller
          control={form.control}
          name="durationMinutes"
          render={({ field, fieldState }) => (
            <SelectField
              label={t('packageDuration')}
              name={field.name}
              value={field.value}
              onChange={field.onChange}
              error={message(fieldState.error)}
            >
              {DURATION_OPTIONS.map((minutes) => (
                <option key={minutes} value={minutes}>
                  {formatDurationMinutes(minutes, lang)}
                </option>
              ))}
            </SelectField>
          )}
        />
      </div>
      <Controller
        control={form.control}
        name="serviceIds"
        render={({ field, fieldState }) => (
          <fieldset className="flex min-w-0 flex-col gap-2" aria-describedby="package-items-hint">
            <legend className="pb-1 text-label font-bold text-text-strong">{t('packageServices')}</legend>
            <p id="package-items-hint" className="text-helper text-text-tertiary">
              {t('packageServicesHint')}
            </p>
            <div className="grid gap-3 sm:grid-cols-2">
              {choices.map((service) => (
                <Checkbox
                  key={service.id}
                  label={localizedName(locale, service.nameAr, service.nameEn)}
                  description={`${formatPrice(service.price, lang, service.currency)} · ${formatDurationMinutes(service.durationMinutes, lang)}`}
                  checked={field.value.includes(service.id)}
                  onChange={(event) =>
                    field.onChange(
                      event.target.checked
                        ? [...field.value, service.id]
                        : field.value.filter((id) => id !== service.id),
                    )
                  }
                />
              ))}
            </div>
            {fieldState.error && (
              <p role="alert" className="text-helper font-medium text-danger-700">
                {message(fieldState.error)}
              </p>
            )}
          </fieldset>
        )}
      />
      {saved && <InlineAlert tone="success" title={tServices('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      {form.formState.errors.root?.server && <InlineAlert tone="danger" title={apiMessage('form')} />}
      <Button type="submit" icon="check" loading={form.formState.isSubmitting} className="self-start">
        {pkg ? t('save') : t('createPackage')}
      </Button>
    </form>
  );
}
