'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import * as z from 'zod';
import { Button } from '@/components/ui/Button';
import { Tabs } from '@/components/ui/Tabs';
import { RadioCard } from '@/components/ui/selection';
import { ConfirmDialog } from '@/components/ui/overlays';
import { TextareaField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { FormTextField, useZodForm } from '@/lib/forms/fields';
import { applyProblemToForm } from '@/lib/forms/problem';
import { email, requiredText, withParams } from '@/lib/forms/validation';

function useFailure() {
  const apiMessage = useApiErrorMessage();
  const [failure, setFailure] = useState<string>();
  return {
    failure,
    clear: () => setFailure(undefined),
    fail: (error: unknown) => setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected')),
  };
}

const createSchema = z.object({
  nameAr: requiredText(120),
  nameEn: requiredText(120),
  slug: z
    .string()
    .trim()
    .toLowerCase()
    .min(1, { error: 'required' })
    .regex(/^[a-z0-9](?:[a-z0-9-]{1,58}[a-z0-9])$/, { error: 'slugInvalid' }),
});

/** Admin creates a shop as a draft (design a-shops "إضافة محل جديد"). */
export function CreateShopForm() {
  const t = useTranslations('adminShops.create');
  const router = useRouter();
  const { failure, clear, fail } = useFailure();
  const form = useZodForm(createSchema, {
    defaultValues: { nameAr: '', nameEn: '', slug: '' },
    mode: 'onSubmit',
  });

  const onSubmit = form.handleSubmit(async (values) => {
    clear();
    try {
      const shop = ensureOk(
        await browserApi.POST('/api/v1/admin/shops', { body: { ...values, timeZone: null } }),
      );
      router.push(`/admin/shops/${shop.id}?tab=services`);
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['nameAr', 'nameEn', 'slug']);
      else fail(error);
    }
  });

  return (
    <form
      method="post"
      noValidate
      onSubmit={onSubmit}
      className="flex max-w-[560px] flex-col gap-5 rounded-card border border-border bg-surface p-6 shadow-e1"
    >
      <FormTextField control={form.control} name="nameAr" label={t('nameAr')} dir="rtl" autoFocus />
      <FormTextField control={form.control} name="nameEn" label={t('nameEn')} dir="ltr" />
      <FormTextField
        control={form.control}
        name="slug"
        label={t('slug')}
        helper={t('slugHelper')}
        dir="ltr"
        autoCapitalize="none"
      />
      <InlineAlert tone="info" title={t('draftNote')} />
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Button type="submit" loading={form.formState.isSubmitting} className="self-start">
        {t('submit')}
      </Button>
    </form>
  );
}

/** Activate / suspend (audited). Suspension asks for confirmation and an optional reason. */
export function ShopStatusActions({ shopId, status }: { shopId: string; status: string }) {
  const t = useTranslations('adminShops.detail');
  const router = useRouter();
  const { failure, clear, fail } = useFailure();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState<string>();

  const change = async (action: 'activate' | 'suspend') => {
    clear();
    setPending(true);
    try {
      const path =
        action === 'activate'
          ? '/api/v1/admin/shops/{shopId}/activate'
          : '/api/v1/admin/shops/{shopId}/suspend';
      ensureOk(
        await browserApi.POST(path, {
          params: { path: { shopId } },
          body: { reason: reason.trim() || null },
        }),
      );
      setConfirmOpen(false);
      setDone(action === 'activate' ? t('activated') : t('suspended'));
      router.refresh();
    } catch (error) {
      fail(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        {status !== 'Active' && (
          <Button
            size="md"
            icon="check"
            loading={pending && !confirmOpen}
            onClick={() => void change('activate')}
          >
            {t('activate')}
          </Button>
        )}
        {status !== 'Suspended' && (
          <Button size="md" variant="danger" icon="pause" onClick={() => setConfirmOpen(true)}>
            {t('suspend')}
          </Button>
        )}
      </div>
      {done && <InlineAlert tone="success" title={done} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t('suspendTitle')}
        body={
          <span className="flex flex-col gap-3">
            <span>{t('suspendBody')}</span>
            <TextareaField
              label={t('reason')}
              value={reason}
              maxLength={500}
              onChange={(event) => setReason(event.target.value)}
            />
          </span>
        }
        confirmLabel={t('suspend')}
        loading={pending}
        onConfirm={() => void change('suspend')}
      />
    </div>
  );
}

const shopRole = z.enum(['ShopOwner', 'ShopStaff']);

const inviteSchema = z.object({ email, role: shopRole });

/** The owner or desk choice shared by both ways of adding a shop account. */
function ShopRoleCards({ field }: { field: UseFormRegisterReturn<'role'> }) {
  const t = useTranslations('adminShops.detail.invite');
  return (
    <fieldset className="flex min-w-0 flex-col gap-2">
      <legend className="pb-2 text-label font-bold text-text-strong">{t('role')}</legend>
      <div className="grid gap-3 sm:grid-cols-2">
        <RadioCard {...field} value="ShopOwner">
          <span className="block font-bold">{t('owner')}</span>
          <span className="block text-helper text-text-tertiary">{t('ownerHint')}</span>
        </RadioCard>
        <RadioCard {...field} value="ShopStaff">
          <span className="block font-bold">{t('staff')}</span>
          <span className="block text-helper text-text-tertiary">{t('staffHint')}</span>
        </RadioCard>
      </div>
    </fieldset>
  );
}

/** A shop's accounts: an emailed invitation, or an account created here with a temporary password (D-139). */
export function ShopAccountForms({ shopId }: { shopId: string }) {
  const t = useTranslations('adminShops.detail.invite');
  return (
    <Tabs
      label={t('modes')}
      items={[
        {
          value: 'email',
          label: t('byEmail'),
          content: (
            <div className="flex flex-col gap-4">
              <p className="text-caption text-text-secondary">{t('body')}</p>
              <InviteShopUserForm shopId={shopId} />
            </div>
          ),
        },
        {
          value: 'manual',
          label: t('manual'),
          content: (
            <div className="flex flex-col gap-4">
              <p className="text-caption text-text-secondary">{t('manualBody')}</p>
              <CreateShopUserForm shopId={shopId} />
            </div>
          ),
        },
      ]}
    />
  );
}

/** Emails an owner or staff invitation for this shop (R-AUTH-02). */
export function InviteShopUserForm({ shopId }: { shopId: string }) {
  const t = useTranslations('adminShops.detail.invite');
  const locale = useLocale();
  const { failure, clear, fail } = useFailure();
  const [sent, setSent] = useState(false);
  const form = useZodForm(inviteSchema, {
    defaultValues: { email: '', role: 'ShopOwner' },
    mode: 'onSubmit',
  });

  const onSubmit = form.handleSubmit(async (values) => {
    clear();
    setSent(false);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/shops/{shopId}/users/invitations', {
          params: { path: { shopId } },
          body: { ...values, locale },
        }),
      );
      setSent(true);
      form.reset({ email: '', role: values.role });
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['email', 'role']);
      else fail(error);
    }
  });

  return (
    <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
      <FormTextField
        control={form.control}
        name="email"
        label={t('email')}
        type="email"
        dir="ltr"
        autoComplete="off"
      />
      <ShopRoleCards field={form.register('role')} />
      {sent && <InlineAlert tone="success" title={t('sent')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Button type="submit" size="md" icon="msg" loading={form.formState.isSubmitting} className="self-start">
        {t('submit')}
      </Button>
    </form>
  );
}

const PASSWORD_ALPHABET = 'abcdefghjkmnpqrstuvwxyz23456789';

/** A temporary password that is easy to read out and type: three groups of four, without look-alike characters. */
export function generateTemporaryPassword(): string {
  const chars: string[] = [];
  while (chars.length < 12) {
    for (const byte of crypto.getRandomValues(new Uint8Array(16))) {
      // 248 = 8 × 31: dropping the rest keeps every character equally likely.
      if (byte < 248 && chars.length < 12) chars.push(PASSWORD_ALPHABET[byte % PASSWORD_ALPHABET.length]!);
    }
  }
  return [0, 4, 8].map((start) => chars.slice(start, start + 4).join('')).join('-');
}

const createUserSchema = z.object({
  displayName: requiredText(60),
  email,
  role: shopRole,
  password: z
    .string()
    .min(1, { error: 'required' })
    .min(10, { error: 'passwordTooShort' })
    .max(256, { error: withParams('tooLong', { max: 256 }) }),
});

/**
 * Creates an owner or desk account with a temporary password and no email (D-139). The admin gives the password to
 * the person, who replaces it at the first sign-in.
 */
export function CreateShopUserForm({ shopId }: { shopId: string }) {
  const t = useTranslations('adminShops.detail.invite');
  const locale = useLocale();
  const { failure, clear, fail } = useFailure();
  const [created, setCreated] = useState<{ email: string; password: string }>();
  const form = useZodForm(createUserSchema, {
    defaultValues: { displayName: '', email: '', role: 'ShopOwner', password: '' },
    mode: 'onSubmit',
  });

  const onSubmit = form.handleSubmit(async (values) => {
    clear();
    setCreated(undefined);
    try {
      const user = ensureOk(
        await browserApi.POST('/api/v1/admin/shops/{shopId}/users', {
          params: { path: { shopId } },
          body: { ...values, locale },
        }),
      );
      setCreated({ email: user.email, password: values.password });
      form.reset({ displayName: '', email: '', role: values.role, password: '' });
    } catch (error) {
      if (error instanceof ApiError && error.isValidation)
        applyProblemToForm(error, form.setError, ['displayName', 'email', 'role', 'password']);
      else fail(error);
    }
  });

  return (
    <form method="post" noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
      <FormTextField control={form.control} name="displayName" label={t('name')} autoComplete="off" />
      <FormTextField
        control={form.control}
        name="email"
        label={t('email')}
        type="email"
        dir="ltr"
        autoComplete="off"
      />
      <ShopRoleCards field={form.register('role')} />
      <div className="flex flex-col gap-2">
        {/* Plain text, because the admin reads it out or copies it; a password field would also invite the
            browser to save it as the admin's own login. */}
        <FormTextField
          control={form.control}
          name="password"
          label={t('temporaryPassword')}
          helper={t('temporaryPasswordHint')}
          dir="ltr"
          autoComplete="off"
          spellCheck={false}
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          icon="refresh"
          className="self-start"
          onClick={() =>
            form.setValue('password', generateTemporaryPassword(), {
              shouldValidate: form.formState.isSubmitted,
            })
          }
        >
          {t('generate')}
        </Button>
      </div>
      {created && (
        <InlineAlert tone="success" title={t('created')}>
          <p>{t('createdBody', { email: created.email })}</p>
          <p dir="ltr" className="font-mono text-body font-bold select-all">
            {created.password}
          </p>
        </InlineAlert>
      )}
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Button
        type="submit"
        size="md"
        icon="plus"
        loading={form.formState.isSubmitting}
        className="self-start"
      >
        {t('create')}
      </Button>
    </form>
  );
}
