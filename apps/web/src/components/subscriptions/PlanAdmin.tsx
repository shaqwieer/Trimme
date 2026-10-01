'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { TextField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { parsePrice } from '@/lib/forms/price';
import { codeToMessageKey } from '@/lib/forms/problem';
import { localizedName } from '@/lib/i18n/localized';
import { type LocalDate } from '@/lib/i18n/localDate';

type Plan = components['schemas']['PlanResponse'];

function useFeedback() {
  const apiMessage = useApiErrorMessage();
  const [notice, setNotice] = useState<{ tone: 'success' | 'danger'; text: string } | null>(null);
  return {
    notice,
    ok: (text: string) => setNotice({ tone: 'success', text }),
    fail: (error: unknown) =>
      setNotice({
        tone: 'danger',
        text: apiMessage(error instanceof ApiError ? error : 'server.unexpected') ?? '',
      }),
    clear: () => setNotice(null),
  };
}

/** Publish / stop offering / archive (final, confirmed). SuperAdmin only (server-enforced). */
export function PlanStateActions({ plan }: { plan: Plan }) {
  const t = useTranslations('subscriptionPlans.detail');
  const locale = useLocale();
  const router = useRouter();
  const { notice, ok, fail, clear } = useFeedback();
  const [pending, setPending] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);

  const change = async (action: 'publish' | 'deactivate' | 'archive') => {
    clear();
    setPending(action);
    try {
      const path = { params: { path: { planId: plan.id } } };
      ensureOk(
        action === 'publish'
          ? await browserApi.POST('/api/v1/admin/subscription-plans/{planId}/publish', path)
          : action === 'deactivate'
            ? await browserApi.POST('/api/v1/admin/subscription-plans/{planId}/deactivate', path)
            : await browserApi.POST('/api/v1/admin/subscription-plans/{planId}/archive', path),
      );
      setConfirm(false);
      ok(t('stateChanged'));
      router.refresh();
    } catch (error) {
      setConfirm(false);
      fail(error);
    } finally {
      setPending(null);
    }
  };

  if (plan.status === 'Archived') return null;
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        {plan.status !== 'Published' && (
          <Button
            size="sm"
            icon="check"
            loading={pending === 'publish'}
            onClick={() => void change('publish')}
          >
            {t('publish')}
          </Button>
        )}
        {plan.status === 'Published' && (
          <Button
            size="sm"
            variant="secondary"
            icon="eyeOff"
            loading={pending === 'deactivate'}
            onClick={() => void change('deactivate')}
          >
            {t('deactivate')}
          </Button>
        )}
        <Button size="sm" variant="danger" icon="book" onClick={() => setConfirm(true)}>
          {t('archive')}
        </Button>
      </div>
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title={t('archiveTitle', { name: localizedName(locale, plan.nameAr, plan.nameEn) })}
        body={t('archiveBody')}
        confirmLabel={t('archive')}
        loading={pending === 'archive'}
        onConfirm={() => void change('archive')}
      />
    </div>
  );
}

/**
 * Adds a price version (append-only, R-SUB-02): amount in the platform currency and the first day it applies, today
 * or later. Existing periods keep the version they were recorded with.
 */
export function PlanPriceForm({ plan, today }: { plan: Plan; today: LocalDate }) {
  const t = useTranslations('subscriptionPlans.detail');
  const tv = useTranslations('validation');
  const router = useRouter();
  const { notice, ok, fail, clear } = useFeedback();
  const [amount, setAmount] = useState('');
  const [effectiveFrom, setEffectiveFrom] = useState(today);
  const [errors, setErrors] = useState<{ amount?: string; effectiveFrom?: string }>({});
  const [pending, setPending] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    clear();
    const price = parsePrice(amount);
    const next = {
      amount: price === null ? tv('priceInvalid') : undefined,
      effectiveFrom: !effectiveFrom || effectiveFrom < today ? tv('dateInPast') : undefined,
    };
    setErrors(next);
    if (next.amount || next.effectiveFrom || price === null) return;
    setPending(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/subscription-plans/{planId}/prices', {
          params: { path: { planId: plan.id } },
          body: { amount: price, effectiveFrom, version: plan.version },
        }),
      );
      setAmount('');
      ok(t('priceAdded'));
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        const field = (name: string) => {
          const code = error.fieldErrors[name]?.[0];
          return code ? tv(codeToMessageKey(code) as 'generic') : undefined;
        };
        setErrors({ amount: field('amount'), effectiveFrom: field('effectiveFrom') });
      } else fail(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <form
      method="post"
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-3"
      data-testid="plan-price-form"
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <TextField
          label={t('amount')}
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          dir="ltr"
          inputMode="decimal"
          autoComplete="off"
          error={errors.amount}
        />
        <TextField
          label={t('effectiveFrom')}
          type="date"
          value={effectiveFrom}
          min={today}
          onChange={(e) => setEffectiveFrom(e.target.value)}
          dir="ltr"
          error={errors.effectiveFrom}
        />
      </div>
      {notice && <InlineAlert tone={notice.tone} title={notice.text} />}
      <Button type="submit" size="sm" icon="plus" loading={pending} className="self-start">
        {t('addPrice')}
      </Button>
    </form>
  );
}

/** Keyboard-operable up/down ordering of the non-archived plans (the API takes the full order). */
export function PlanOrderButtons({ ids, index, name }: { ids: string[]; index: number; name: string }) {
  const t = useTranslations('subscriptionPlans');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<string>();

  const move = async (delta: -1 | 1) => {
    const order = [...ids];
    const [moved] = order.splice(index, 1);
    order.splice(index + delta, 0, moved as string);
    setPending(true);
    setFailure(undefined);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/subscription-plans/order', { body: { orderedIds: order } }),
      );
      router.refresh();
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex items-center gap-1">
      <IconButton
        icon="chevD"
        className="rotate-180"
        label={t('moveUp', { name })}
        disabled={index === 0 || pending}
        onClick={() => void move(-1)}
      />
      <IconButton
        icon="chevD"
        label={t('moveDown', { name })}
        disabled={index === ids.length - 1 || pending}
        onClick={() => void move(1)}
      />
      {failure && (
        <span role="alert" className="text-helper text-danger-700">
          {failure}
        </span>
      )}
    </div>
  );
}
