'use client';

import Image from 'next/image';
import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Controller } from 'react-hook-form';
import { z } from 'zod';
import { IMAGE_TYPES, MAX_IMAGE_BYTES } from '@/components/shops/ShopImagesEditor';
import { Button } from '@/components/ui/Button';
import { PhoneField, SelectField, Switch, TextareaField } from '@/components/ui/inputs';
import { ConfirmDialog, Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { UploadDropZone } from '@/components/ui/UploadDropZone';
import { isValidSaudiMobile, normalizeSaudiMobile, toE164 } from '@/components/ui/digits';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import {
  FormPhoneField,
  FormTextareaField,
  FormTextField,
  useValidationMessage,
  useZodForm,
} from '@/lib/forms/fields';
import { applyProblemToForm, codeToMessageKey } from '@/lib/forms/problem';
import { requiredText, withParams } from '@/lib/forms/validation';

type Professional = components['schemas']['AdminProfessionalResponse'];
type WhatsApp = components['schemas']['AdminWhatsAppResponse'];

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, { error: withParams('tooLong', { max }) });
const slug = z
  .string()
  .trim()
  .toLowerCase()
  .refine((value) => value === '' || /^[a-z0-9](?:[a-z0-9-]{1,58}[a-z0-9])$/.test(value), {
    error: 'slugInvalid',
  });
/** Optional Saudi mobile: empty, or 9 national digits starting with 5. */
const optionalMobile = z
  .string()
  .transform((value) => normalizeSaudiMobile(value))
  .refine((national) => national === '' || isValidSaudiMobile(national), { error: 'phoneInvalid' });

const profileFields = {
  nameAr: requiredText(120),
  nameEn: requiredText(120),
  slug,
  specialtyAr: optionalText(80),
  specialtyEn: optionalText(80),
  bioAr: optionalText(600),
  bioEn: optionalText(600),
};
const PROFILE_KEYS = ['nameAr', 'nameEn', 'slug', 'specialtyAr', 'specialtyEn', 'bioAr', 'bioEn'] as const;

function useFailure() {
  const apiMessage = useApiErrorMessage();
  const [failure, setFailure] = useState<string>();
  return {
    failure,
    clear: () => setFailure(undefined),
    fail: (error: unknown) => setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected')),
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

function ProfileFields({
  control,
}: {
  control: ReturnType<typeof useZodForm<z.ZodObject<typeof profileFields>>>['control'];
}) {
  const t = useTranslations('adminProfessionals.create');
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <FormTextField control={control} name="nameAr" label={t('nameAr')} dir="rtl" />
      <FormTextField control={control} name="nameEn" label={t('nameEn')} dir="ltr" />
      <FormTextField control={control} name="specialtyAr" label={t('specialtyAr')} dir="rtl" optional />
      <FormTextField control={control} name="specialtyEn" label={t('specialtyEn')} dir="ltr" optional />
      <FormTextareaField
        control={control}
        name="bioAr"
        label={t('bioAr')}
        dir="rtl"
        optional
        maxLength={600}
      />
      <FormTextareaField
        control={control}
        name="bioEn"
        label={t('bioEn')}
        dir="ltr"
        optional
        maxLength={600}
      />
      <FormTextField
        control={control}
        name="slug"
        label={t('slug')}
        helper={t('slugHelper')}
        dir="ltr"
        autoCapitalize="none"
        optional
      />
    </div>
  );
}

const createSchema = z.object({
  ...profileFields,
  shopId: z.string().min(1, { error: 'required' }),
  whatsApp: optionalMobile,
  notificationsEnabled: z.boolean(),
});

/**
 * Admin creates a professional (DV-A07). The shop is chosen here, once; there is no later change of shop (D-011).
 * The WhatsApp number is entered once and afterwards only shown masked.
 */
export function CreateProfessionalForm({
  shops,
  defaultShopId,
}: {
  shops: Array<{ id: string; name: string }>;
  defaultShopId?: string;
}) {
  const t = useTranslations('adminProfessionals.create');
  const router = useRouter();
  const message = useValidationMessage();
  const { failure, clear, fail } = useFailure();
  const form = useZodForm(createSchema, {
    mode: 'onSubmit',
    defaultValues: {
      shopId: defaultShopId ?? '',
      nameAr: '',
      nameEn: '',
      slug: '',
      specialtyAr: '',
      specialtyEn: '',
      bioAr: '',
      bioEn: '',
      whatsApp: '',
      notificationsEnabled: true,
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    clear();
    try {
      const created = ensureOk(
        await browserApi.POST('/api/v1/admin/professionals', {
          body: {
            shopId: values.shopId,
            nameAr: values.nameAr,
            nameEn: values.nameEn,
            slug: orNull(values.slug),
            specialtyAr: orNull(values.specialtyAr),
            specialtyEn: orNull(values.specialtyEn),
            bioAr: orNull(values.bioAr),
            bioEn: orNull(values.bioEn),
            whatsAppNumber: values.whatsApp ? toE164(values.whatsApp) : null,
            notificationsEnabled: values.whatsApp ? values.notificationsEnabled : false,
          },
        }),
      );
      router.push(`/admin/professionals/${created.id}`);
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        const fields = { ...error.fieldErrors };
        if (fields.whatsAppNumber) fields.whatsApp = fields.whatsAppNumber;
        applyProblemToForm(
          new ApiError(error.status, { errorCode: error.errorCode, errors: fields }),
          form.setError,
          [...PROFILE_KEYS, 'shopId', 'whatsApp', 'notificationsEnabled'],
        );
      } else fail(error);
    }
  });

  return (
    <form
      noValidate
      onSubmit={onSubmit}
      className="flex max-w-[900px] flex-col gap-5 rounded-card border border-border bg-surface p-6 shadow-e1"
      data-testid="create-professional-form"
    >
      <Controller
        control={form.control}
        name="shopId"
        render={({ field, fieldState }) => (
          <SelectField
            label={t('shop')}
            name={field.name}
            value={field.value}
            onChange={field.onChange}
            helper={t('shopHelper')}
            error={message(fieldState.error)}
          >
            <option value="">{t('shopPlaceholder')}</option>
            {shops.map((shop) => (
              <option key={shop.id} value={shop.id}>
                {shop.name}
              </option>
            ))}
          </SelectField>
        )}
      />
      <ProfileFields control={form.control as never} />
      <FormPhoneField
        control={form.control}
        name="whatsApp"
        label={t('whatsapp')}
        helper={t('whatsappHelper')}
        optional
      />
      <Controller
        control={form.control}
        name="notificationsEnabled"
        render={({ field }) => (
          <Switch checked={field.value} onCheckedChange={field.onChange} label={t('notifications')} />
        )}
      />
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Button type="submit" icon="plus" loading={form.formState.isSubmitting} className="self-start">
        {t('submit')}
      </Button>
    </form>
  );
}

const editSchema = z.object(profileFields);

/** Profile edit. Deliberately no shop field: the shop is shown read-only on the page (R-NEG-01). */
export function EditProfessionalForm({ professional }: { professional: Professional }) {
  const t = useTranslations('adminProfessionals.detail');
  const router = useRouter();
  const { failure, clear, fail } = useFailure();
  const [saved, setSaved] = useState(false);
  const form = useZodForm(editSchema, {
    mode: 'onSubmit',
    defaultValues: {
      nameAr: professional.nameAr,
      nameEn: professional.nameEn,
      slug: professional.slug,
      specialtyAr: professional.specialtyAr ?? '',
      specialtyEn: professional.specialtyEn ?? '',
      bioAr: professional.bioAr ?? '',
      bioEn: professional.bioEn ?? '',
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    clear();
    setSaved(false);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/professionals/{professionalId}', {
          params: { path: { professionalId: professional.id } },
          body: {
            nameAr: values.nameAr,
            nameEn: values.nameEn,
            slug: orNull(values.slug),
            specialtyAr: orNull(values.specialtyAr),
            specialtyEn: orNull(values.specialtyEn),
            bioAr: orNull(values.bioAr),
            bioEn: orNull(values.bioEn),
            version: professional.version,
          },
        }),
      );
      setSaved(true);
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, [...PROFILE_KEYS]);
      else fail(error);
    }
  });

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5" data-testid="edit-professional-form">
      <ProfileFields control={form.control} />
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Button type="submit" icon="check" loading={form.formState.isSubmitting} className="self-start">
        {t('save')}
      </Button>
    </form>
  );
}

/** Disable (confirm + optional reason) or enable again; both audited. */
export function ProfessionalStatusActions({ id, status }: { id: string; status: 'Active' | 'Disabled' }) {
  const t = useTranslations('adminProfessionals.detail');
  const router = useRouter();
  const { failure, clear, fail } = useFailure();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState<string>();

  const change = async (enable: boolean) => {
    clear();
    setPending(true);
    try {
      const path = { params: { path: { professionalId: id } }, body: { reason: reason.trim() || null } };
      ensureOk(
        enable
          ? await browserApi.POST('/api/v1/admin/professionals/{professionalId}/enable', path)
          : await browserApi.POST('/api/v1/admin/professionals/{professionalId}/disable', path),
      );
      setOpen(false);
      setDone(enable ? t('enabled') : t('disabled'));
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      {status === 'Active' ? (
        <Button variant="danger" size="md" icon="ban" onClick={() => setOpen(true)} className="self-start">
          {t('disable')}
        </Button>
      ) : (
        <Button
          size="md"
          icon="check"
          loading={pending}
          onClick={() => void change(true)}
          className="self-start"
        >
          {t('enable')}
        </Button>
      )}
      {done && <InlineAlert tone="success" title={done} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={t('disableTitle')}
        body={
          <span className="flex flex-col gap-3">
            <span>{t('disableBody')}</span>
            <TextareaField
              label={t('reason')}
              value={reason}
              maxLength={500}
              onChange={(event) => setReason(event.target.value)}
            />
          </span>
        }
        confirmLabel={t('disable')}
        loading={pending}
        onConfirm={() => void change(false)}
      />
    </div>
  );
}

/** Photo upload/remove (stored in the database, D-064). */
export function ProfessionalAvatarEditor({
  id,
  avatarUrl,
  name,
}: {
  id: string;
  avatarUrl: string | null;
  name: string;
}) {
  const t = useTranslations('adminProfessionals.detail');
  const tv = useTranslations('validation') as unknown as (key: string) => string;
  const router = useRouter();
  const { failure, clear, fail } = useFailure();
  const [fileError, setFileError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<unknown>) => {
    clear();
    setFileError(undefined);
    setBusy(true);
    try {
      ensureOk((await action()) as { response: Response });
      router.refresh();
    } catch (error) {
      const code = error instanceof ApiError ? error.fieldErrors.file?.[0] : undefined;
      if (code) setFileError(tv(codeToMessageKey(code)));
      else fail(error);
    } finally {
      setBusy(false);
    }
  };

  const path = { params: { path: { professionalId: id } } };
  return (
    <div className="flex flex-wrap items-start gap-4" data-testid="professional-avatar">
      {avatarUrl ? (
        <Image
          src={avatarUrl}
          alt={name}
          width={96}
          height={96}
          unoptimized
          className="size-24 rounded-full border border-border object-cover"
        />
      ) : (
        <span
          className="flex size-24 items-center justify-center rounded-full bg-bg-muted text-h2 font-bold text-navy-900"
          aria-hidden="true"
        >
          {name.slice(0, 1)}
        </span>
      )}
      <div className="flex min-w-[240px] flex-1 flex-col gap-2">
        <UploadDropZone
          label={t('photo')}
          prompt={t('uploadPhoto')}
          accept={IMAGE_TYPES}
          maxBytes={MAX_IMAGE_BYTES}
          recommended={{ width: 512, height: 512 }}
          onFileSelected={(file) => {
            const body = new FormData();
            body.append('file', file);
            void run(() =>
              browserApi.PUT('/api/v1/admin/professionals/{professionalId}/avatar', {
                ...path,
                body: body as unknown as { file?: string },
              }),
            );
          }}
        />
        {avatarUrl && (
          <Button
            variant="ghost"
            size="sm"
            icon="trash"
            loading={busy}
            onClick={() =>
              void run(() => browserApi.DELETE('/api/v1/admin/professionals/{professionalId}/avatar', path))
            }
            className="self-start"
          >
            {t('removePhoto')}
          </Button>
        )}
        {fileError && <InlineAlert tone="danger" title={fileError} />}
        {failure && <InlineAlert tone="danger" title={failure} />}
      </div>
    </div>
  );
}

/**
 * WhatsApp settings (spec §8): the number only ever masked, a notification toggle, correction, and an audited
 * reveal that needs a reason. The revealed number lives only in this component's state and is never cached.
 */
export function WhatsAppSettings({
  id,
  whatsApp,
  canManage,
  canReveal,
}: {
  id: string;
  whatsApp: WhatsApp;
  canManage: boolean;
  canReveal: boolean;
}) {
  const t = useTranslations('adminProfessionals.whatsapp');
  const tv = useTranslations('validation');
  const router = useRouter();
  const { failure, clear, fail } = useFailure();
  const [editing, setEditing] = useState(false);
  const [replaceNumber, setReplaceNumber] = useState(!whatsApp.masked);
  const [national, setNational] = useState('');
  const [notifications, setNotifications] = useState(whatsApp.notificationsEnabled);
  const [numberError, setNumberError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [revealOpen, setRevealOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string>();
  const [revealing, setRevealing] = useState(false);
  const [revealed, setRevealed] = useState<string>();
  const path = { params: { path: { professionalId: id } } };

  const save = async () => {
    clear();
    setSaved(false);
    setNumberError(undefined);
    let number: string | null = null;
    if (replaceNumber) {
      if (national !== '' && !isValidSaudiMobile(national)) {
        setNumberError(tv('phoneInvalid'));
        return;
      }
      number = national === '' ? null : toE164(national);
    }
    setSaving(true);
    try {
      // Keeping the number changes only the toggle; the admin never needs to see the number for that.
      ensureOk(
        await browserApi.PUT('/api/v1/admin/professionals/{professionalId}/whatsapp', {
          ...path,
          body: replaceNumber
            ? {
                whatsAppNumber: number,
                notificationsEnabled: number ? notifications : false,
                keepCurrentNumber: false,
              }
            : { whatsAppNumber: null, notificationsEnabled: notifications, keepCurrentNumber: true },
        }),
      );
      setSaved(true);
      setEditing(false);
      setNational('');
      router.refresh();
    } catch (error) {
      const code =
        error instanceof ApiError
          ? (error.fieldErrors.whatsAppNumber ?? error.fieldErrors.notificationsEnabled)?.[0]
          : undefined;
      if (code) setNumberError(tv(codeToMessageKey(code) as 'generic'));
      else fail(error);
    } finally {
      setSaving(false);
    }
  };

  const reveal = async () => {
    if (reason.trim().length < 5) {
      setReasonError(tv('reasonRequired'));
      return;
    }
    setReasonError(undefined);
    setRevealing(true);
    try {
      const result = ensureOk(
        await browserApi.POST('/api/v1/admin/professionals/{professionalId}/whatsapp/reveal', {
          ...path,
          body: { reason: reason.trim() },
        }),
      );
      setRevealed(result.number);
      setRevealOpen(false);
      setReason('');
    } catch (error) {
      fail(error);
      setRevealOpen(false);
    } finally {
      setRevealing(false);
    }
  };

  return (
    <div className="flex flex-col gap-4" data-testid="whatsapp-settings">
      <dl className="grid gap-3 text-caption sm:grid-cols-3">
        <div>
          <dt className="text-helper text-text-tertiary">{t('number')}</dt>
          <dd
            dir="ltr"
            className="text-start font-latin font-semibold text-text-primary"
            data-testid="whatsapp-masked"
          >
            {revealed ?? whatsApp.masked ?? t('none')}
          </dd>
        </div>
        <div>
          <dt className="sr-only">{t('title')}</dt>
          <dd className="font-semibold text-text-primary">
            {whatsApp.notificationsEnabled ? t('notificationsOn') : t('notificationsOff')}
          </dd>
        </div>
        {whatsApp.masked && (
          <div>
            <dt className="sr-only">{t('verification.Unverified')}</dt>
            <dd className="text-text-secondary">{t(`verification.${whatsApp.verification}`)}</dd>
          </div>
        )}
      </dl>

      <div className="flex flex-wrap gap-2">
        {canReveal && whatsApp.masked && !revealed && (
          <Button variant="outline" size="md" icon="eyeOff" onClick={() => setRevealOpen(true)}>
            {t('reveal')}
          </Button>
        )}
        {revealed && (
          <Button variant="ghost" size="md" onClick={() => setRevealed(undefined)}>
            {t('hide')}
          </Button>
        )}
        {canManage && !editing && (
          <Button variant="secondary" size="md" icon="edit" onClick={() => setEditing(true)}>
            {t('edit')}
          </Button>
        )}
      </div>

      {editing && (
        <div className="flex flex-col gap-4 rounded-card border border-border p-4">
          {whatsApp.masked && (
            <Switch
              checked={!replaceNumber}
              onCheckedChange={(keep) => setReplaceNumber(!keep)}
              label={t('keepNumber')}
            />
          )}
          {replaceNumber && (
            <PhoneField
              label={t('newNumber')}
              helper={t('newNumberHelper')}
              optional
              value={national}
              onValueChange={(next) => setNational(next)}
              error={numberError}
            />
          )}
          {!replaceNumber && numberError && <InlineAlert tone="danger" title={numberError} />}
          <Switch checked={notifications} onCheckedChange={setNotifications} label={t('notificationsOn')} />
          <div className="flex gap-2">
            <Button size="md" icon="check" loading={saving} onClick={() => void save()}>
              {t('save')}
            </Button>
            <Button variant="ghost" size="md" onClick={() => setEditing(false)}>
              {t('hide')}
            </Button>
          </div>
        </div>
      )}

      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}

      <Dialog
        open={revealOpen}
        onOpenChange={setRevealOpen}
        title={t('revealTitle')}
        description={t('revealBody')}
        footer={
          <Button size="md" loading={revealing} onClick={() => void reveal()}>
            {t('revealSubmit')}
          </Button>
        }
      >
        <TextareaField
          label={t('reason')}
          value={reason}
          maxLength={500}
          onChange={(event) => setReason(event.target.value)}
          error={reasonError}
        />
      </Dialog>
    </div>
  );
}
