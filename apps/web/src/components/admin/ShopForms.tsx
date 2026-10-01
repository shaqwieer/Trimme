'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import * as z from 'zod';
import { Button } from '@/components/ui/Button';
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
import { email, requiredText } from '@/lib/forms/validation';

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
      router.push(`/admin/shops/${shop.id}`);
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

const inviteSchema = z.object({ email, role: z.enum(['ShopOwner', 'ShopStaff']) });

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
      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="pb-2 text-label font-bold text-text-strong">{t('role')}</legend>
        <div className="grid gap-3 sm:grid-cols-2">
          <RadioCard {...form.register('role')} value="ShopOwner">
            <span className="block font-bold">{t('owner')}</span>
            <span className="block text-helper text-text-tertiary">{t('ownerHint')}</span>
          </RadioCard>
          <RadioCard {...form.register('role')} value="ShopStaff">
            <span className="block font-bold">{t('staff')}</span>
            <span className="block text-helper text-text-tertiary">{t('staffHint')}</span>
          </RadioCard>
        </div>
      </fieldset>
      {sent && <InlineAlert tone="success" title={t('sent')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      <Button type="submit" size="md" icon="msg" loading={form.formState.isSubmitting} className="self-start">
        {t('submit')}
      </Button>
    </form>
  );
}
