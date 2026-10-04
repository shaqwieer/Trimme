'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Controller } from 'react-hook-form';
import * as z from 'zod';
import { Button } from '@/components/ui/Button';
import { Checkbox, SelectField, Switch } from '@/components/ui/inputs';
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
import { formatDurationMinutes } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Category = components['schemas']['ServiceCategoryResponse'];
type Service = components['schemas']['ShopServiceResponse'];

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, { error: withParams('tooLong', { max }) });

export const serviceSchema = z.object({
  nameAr: requiredText(120),
  nameEn: optionalText(120),
  descriptionAr: optionalText(500),
  descriptionEn: optionalText(500),
  categoryId: z.string(),
  price: z.string().refine((value) => parsePrice(value) !== null, { error: 'priceInvalid' }),
  durationMinutes: z
    .string()
    .refine((value) => DURATION_OPTIONS.includes(Number(value)), { error: 'durationInvalid' }),
  onlineBookable: z.boolean(),
});

const FIELDS = [
  'nameAr',
  'nameEn',
  'descriptionAr',
  'descriptionEn',
  'categoryId',
  'price',
  'durationMinutes',
] as const;

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

/** A barber who can be picked to do the service (the admin's form, D-127). */
export type ServiceFormProfessional = { id: string; name: string };

/**
 * The shop's own service form (DV-A01, DV-S03): Arabic name required, English optional, the shop's own price and a
 * duration in 5-minute steps. Also used by the admin's support override (with a reason) and by the admin building a
 * shop's catalogue (D-127, with the shop's barbers to pick) through `onSubmitValues`.
 */
export function ServiceForm({
  categories,
  service,
  submitLabel,
  onSubmitValues,
  reasonLabel,
  professionals,
  assignedIds,
}: {
  categories: Category[];
  service?: Service | components['schemas']['AdminServiceResponse'];
  submitLabel?: string;
  /**
   * Custom save (admin). Defaults to the shop's own create/update. Resolve to `'navigated'` when it moved to another
   * page, so the form does not refresh this one.
   */
  onSubmitValues?: (
    body: components['schemas']['UpdateShopServiceRequest'],
    reason: string,
    professionalIds: string[],
  ) => Promise<void | 'navigated'>;
  /** Label of a required reason field (admin support override). */
  reasonLabel?: string;
  /** The shop's barbers, to pick who does the service (admin, D-127). Without it the field is not shown. */
  professionals?: ServiceFormProfessional[];
  /** The barbers who do it now; a new service starts with every barber picked. */
  assignedIds?: string[];
}) {
  const t = useTranslations('shopServices.form');
  const tServices = useTranslations('shopServices');
  const tCatalog = useTranslations('catalog');
  const locale = useLocale();
  const router = useRouter();
  const message = useValidationMessage();
  const apiMessage = useApiErrorMessage();
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);

  const schema = serviceSchema.extend({
    reason: reasonLabel ? z.string().trim().min(5, { error: 'reasonRequired' }).max(500) : z.string(),
    professionalIds: z.array(z.string()),
  });
  const form = useZodForm(schema, {
    mode: 'onSubmit',
    defaultValues: {
      nameAr: service?.nameAr ?? '',
      nameEn: service?.nameEn ?? '',
      descriptionAr: service?.descriptionAr ?? '',
      descriptionEn: service?.descriptionEn ?? '',
      categoryId: service?.categoryId ?? '',
      price: service ? String(service.price) : '',
      durationMinutes: String(service?.durationMinutes ?? 30),
      onlineBookable: service?.onlineBookable ?? true,
      reason: '',
      professionalIds: assignedIds ?? (service ? [] : (professionals ?? []).map((p) => p.id)),
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setFailure(undefined);
    setSaved(false);
    const body = {
      nameAr: values.nameAr,
      nameEn: orNull(values.nameEn),
      descriptionAr: orNull(values.descriptionAr),
      descriptionEn: orNull(values.descriptionEn),
      categoryId: values.categoryId || null,
      price: parsePrice(values.price) ?? 0,
      durationMinutes: Number(values.durationMinutes),
      onlineBookable: values.onlineBookable,
      version: service?.version ?? 0,
    };
    try {
      if (onSubmitValues) {
        if ((await onSubmitValues(body, values.reason, values.professionalIds)) === 'navigated') return;
      } else if (service) {
        ensureOk(
          await browserApi.PUT('/api/v1/shop/services/{serviceId}', {
            params: { path: { serviceId: service.id } },
            body,
          }),
        );
      } else {
        const created = ensureOk(await browserApi.POST('/api/v1/shop/services', { body }));
        router.push(`/shop/services/${created.id}`);
        return;
      }
      setSaved(true);
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, [...FIELDS, 'reason', 'professionalIds']);
      else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    }
  });

  return (
    <form
      method="post"
      noValidate
      onSubmit={onSubmit}
      className="flex flex-col gap-5"
      data-testid="service-form"
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
        <Controller
          control={form.control}
          name="categoryId"
          render={({ field, fieldState }) => (
            <SelectField
              label={t('category')}
              name={field.name}
              value={field.value}
              onChange={field.onChange}
              optional
              error={message(fieldState.error)}
            >
              <option value="">{tCatalog('noCategory')}</option>
              {categories.map((category) => (
                <option key={category.id} value={category.id}>
                  {localizedName(locale, category.nameAr, category.nameEn)}
                </option>
              ))}
            </SelectField>
          )}
        />
        <FormTextField
          control={form.control}
          name="price"
          label={t('price')}
          helper={t('priceHelper')}
          dir="ltr"
          inputMode="decimal"
          autoComplete="off"
        />
        <Controller
          control={form.control}
          name="durationMinutes"
          render={({ field, fieldState }) => (
            <SelectField
              label={t('duration')}
              name={field.name}
              value={field.value}
              onChange={field.onChange}
              error={message(fieldState.error)}
            >
              {DURATION_OPTIONS.map((minutes) => (
                <option key={minutes} value={minutes}>
                  {formatDurationMinutes(minutes, locale === 'en' ? 'en' : 'ar')}
                </option>
              ))}
            </SelectField>
          )}
        />
      </div>
      <Controller
        control={form.control}
        name="onlineBookable"
        render={({ field }) => (
          <div className="flex flex-col gap-1">
            <Switch checked={field.value} onCheckedChange={field.onChange} label={t('onlineBookable')} />
            <span className="text-helper text-text-tertiary">{t('onlineBookableHint')}</span>
          </div>
        )}
      />
      {professionals && (
        <Controller
          control={form.control}
          name="professionalIds"
          render={({ field, fieldState }) => (
            <fieldset className="flex min-w-0 flex-col gap-2" data-testid="service-professionals">
              <legend className="pb-1 text-label font-bold text-text-strong">{t('professionals')}</legend>
              <p className="text-helper text-text-tertiary">{t('professionalsHint')}</p>
              {professionals.length === 0 ? (
                <InlineAlert tone="info" title={t('noShopProfessionals')} />
              ) : (
                <div className="grid gap-2 sm:grid-cols-2">
                  {professionals.map((professional) => (
                    <Checkbox
                      key={professional.id}
                      label={professional.name}
                      checked={field.value.includes(professional.id)}
                      onChange={(event) =>
                        field.onChange(
                          event.target.checked
                            ? [...field.value, professional.id]
                            : field.value.filter((id) => id !== professional.id),
                        )
                      }
                    />
                  ))}
                </div>
              )}
              {professionals.length > 0 && field.value.length === 0 && (
                <InlineAlert tone="warning" title={t('noProfessionalsPicked')} />
              )}
              {fieldState.error && <InlineAlert tone="danger" title={message(fieldState.error)} />}
            </fieldset>
          )}
        />
      )}
      {reasonLabel && (
        <FormTextareaField control={form.control} name="reason" label={reasonLabel} maxLength={500} />
      )}
      {saved && <InlineAlert tone="success" title={tServices('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      {form.formState.errors.root?.server && <InlineAlert tone="danger" title={apiMessage('form')} />}
      <Button type="submit" icon="check" loading={form.formState.isSubmitting} className="self-start">
        {submitLabel ?? (service ? t('save') : t('create'))}
      </Button>
    </form>
  );
}
