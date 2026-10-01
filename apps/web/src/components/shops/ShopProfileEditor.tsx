'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Controller } from 'react-hook-form';
import * as z from 'zod';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { Checkbox, SelectField, Switch } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { FormTextareaField, FormTextField, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm } from '@/lib/forms/problem';
import { requiredText, withParams } from '@/lib/forms/validation';
import {
  type EditorMode,
  SHOP_AMENITIES,
  SHOP_CATEGORIES,
  type ShopProfileData,
  type ShopProfileField,
  shopApi,
} from './shopApi';

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, { error: withParams('tooLong', { max }) });

const schema = z.object({
  nameAr: requiredText(120),
  nameEn: requiredText(120),
  descriptionAr: optionalText(1000),
  descriptionEn: optionalText(1000),
  category: z.enum(SHOP_CATEGORIES as [string, ...string[]]),
  publicPhone: z
    .string()
    .trim()
    .refine((value) => value === '' || /^\+?[0-9٠-٩\s()-]{7,20}$/.test(value), {
      error: 'businessPhoneInvalid',
    }),
  amenities: z.array(z.string()),
  isVerified: z.boolean(),
});

const FIELDS = [
  'nameAr',
  'nameEn',
  'descriptionAr',
  'descriptionEn',
  'category',
  'publicPhone',
  'amenities',
] as const;

/** A small "locked by the admin" note under a field the policy does not open to the shop (DV-S16). */
export function LockNote() {
  const t = useTranslations('shopProfile');
  return (
    <span className="inline-flex items-center gap-1 text-helper text-text-tertiary">
      <Icon name="shield" className="size-3.5" />
      {t('locked')}
    </span>
  );
}

type ShopProfileEditorProps = {
  mode: EditorMode;
  profile: ShopProfileData;
  /** False for a user without the edit permission: the form is read-only. */
  canEdit: boolean;
};

/**
 * Profile form shared by the admin shop detail and the shop's settings (s-settings "ملف المحل"). For the shop,
 * fields outside the admin policy are read-only with a shield, and the server enforces the same policy (R-SHP-03).
 */
export function ShopProfileEditor({ mode, profile, canEdit }: ShopProfileEditorProps) {
  const t = useTranslations('shopProfile');
  const tCategory = useTranslations('shopCategory');
  const tAmenity = useTranslations('amenity');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<string>();
  const isAdmin = mode.kind === 'admin';
  const editable = (field: ShopProfileField) =>
    canEdit && (isAdmin || profile.editableFields.includes(field));

  const form = useZodForm(schema, {
    mode: 'onSubmit',
    defaultValues: {
      nameAr: profile.nameAr,
      nameEn: profile.nameEn,
      descriptionAr: profile.descriptionAr ?? '',
      descriptionEn: profile.descriptionEn ?? '',
      category: profile.category,
      publicPhone: profile.publicPhone ?? '',
      amenities: [...profile.amenities],
      isVerified: profile.isVerified,
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setSaved(false);
    setFailure(undefined);
    try {
      await shopApi(mode).saveProfile(
        {
          nameAr: values.nameAr,
          nameEn: values.nameEn,
          descriptionAr: values.descriptionAr || null,
          descriptionEn: values.descriptionEn || null,
          category: values.category as ShopProfileData['category'],
          publicPhone: values.publicPhone || null,
          amenities: values.amenities as ShopProfileData['amenities'],
        },
        profile.version,
        values.isVerified,
      );
      setSaved(true);
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, [...FIELDS]);
      else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    }
  });

  const lock = (field: ShopProfileField) => (!isAdmin && !editable(field) ? <LockNote /> : undefined);

  return (
    <form
      method="post"
      noValidate
      onSubmit={onSubmit}
      className="flex flex-col gap-5"
      data-testid="shop-profile-form"
    >
      {!isAdmin && <p className="text-helper text-text-tertiary">{t('lockedHint')}</p>}
      <div className="grid gap-4 md:grid-cols-2">
        <FormTextField
          control={form.control}
          name="nameAr"
          label={t('nameAr')}
          dir="rtl"
          disabled={!editable('Name')}
          helper={lock('Name')}
        />
        <FormTextField
          control={form.control}
          name="nameEn"
          label={t('nameEn')}
          dir="ltr"
          disabled={!editable('Name')}
          helper={lock('Name')}
        />
        <FormTextareaField
          control={form.control}
          name="descriptionAr"
          label={t('descriptionAr')}
          dir="rtl"
          optional
          maxLength={1000}
          disabled={!editable('Description')}
          helper={lock('Description')}
        />
        <FormTextareaField
          control={form.control}
          name="descriptionEn"
          label={t('descriptionEn')}
          dir="ltr"
          optional
          maxLength={1000}
          disabled={!editable('Description')}
          helper={lock('Description')}
        />
        <Controller
          control={form.control}
          name="category"
          render={({ field }) => (
            <SelectField
              label={t('category')}
              name={field.name}
              value={field.value}
              onChange={field.onChange}
              disabled={!editable('Category')}
              helper={lock('Category')}
            >
              {SHOP_CATEGORIES.map((category) => (
                <option key={category} value={category}>
                  {tCategory(category)}
                </option>
              ))}
            </SelectField>
          )}
        />
        <FormTextField
          control={form.control}
          name="publicPhone"
          label={t('publicPhone')}
          dir="ltr"
          inputMode="tel"
          optional
          disabled={!editable('PublicPhone')}
          helper={lock('PublicPhone') ?? t('publicPhoneHelper')}
        />
      </div>

      <Controller
        control={form.control}
        name="amenities"
        render={({ field }) => (
          <fieldset className="flex min-w-0 flex-col gap-2" disabled={!editable('Amenities')}>
            <legend className="flex items-center gap-2 pb-2 text-label font-bold text-text-strong">
              {t('amenities')} {lock('Amenities')}
            </legend>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {SHOP_AMENITIES.map((amenity) => (
                <Checkbox
                  key={amenity}
                  label={tAmenity(amenity)}
                  checked={field.value.includes(amenity)}
                  onChange={(event) =>
                    field.onChange(
                      event.target.checked
                        ? [...field.value, amenity]
                        : field.value.filter((value) => value !== amenity),
                    )
                  }
                />
              ))}
            </div>
          </fieldset>
        )}
      />

      {isAdmin && (
        <Controller
          control={form.control}
          name="isVerified"
          render={({ field }) => (
            <div className="flex flex-col gap-1">
              <Switch
                checked={field.value}
                onCheckedChange={field.onChange}
                label={t('verified')}
                disabled={!canEdit}
              />
              <span className="text-helper text-text-tertiary">{t('verifiedHint')}</span>
            </div>
          )}
        />
      )}

      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      {form.formState.errors.root?.server && <InlineAlert tone="danger" title={apiMessage('form')} />}
      {canEdit && (
        <Button type="submit" icon="check" loading={form.formState.isSubmitting} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
