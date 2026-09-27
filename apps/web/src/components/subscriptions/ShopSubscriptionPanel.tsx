'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, TextareaField, TextField } from '@/components/ui/inputs';
import { Dialog } from '@/components/ui/overlays';
import { SegmentedControl } from '@/components/ui/selection.client';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { parsePrice } from '@/lib/forms/price';
import { codeToMessageKey } from '@/lib/forms/problem';
import type { AppLocale } from '@/lib/i18n/format';
import { formatPrice } from '@/lib/i18n/format';
import { type LocalDate } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { longDate, periodEnd, priceOn } from './periods';

type Subscription = components['schemas']['AdminShopSubscriptionResponse'];
type Plan = components['schemas']['PlanResponse'];

type FieldErrors = Partial<
  Record<'planId' | 'startDate' | 'durationDays' | 'price' | 'endDate' | 'reason' | 'notes', string>
>;

function useFieldErrors() {
  const tv = useTranslations('validation');
  const apiMessage = useApiErrorMessage();
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failure, setFailure] = useState<string>();
  return {
    errors,
    failure,
    setErrors,
    reset: () => {
      setErrors({});
      setFailure(undefined);
    },
    /** Field errors from a validation problem; anything else becomes a form-level message. */
    handle: (error: unknown) => {
      if (error instanceof ApiError && error.isValidation) {
        const next: FieldErrors = {};
        for (const [field, codes] of Object.entries(error.fieldErrors))
          next[field as keyof FieldErrors] = tv(codeToMessageKey(codes[0]) as 'generic');
        setErrors(next);
      } else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    },
  };
}

/**
 * Record an activation (no subscription yet) or a renewal (DV-S05): the plan, a start date and the plan's own period;
 * the end date and the price version in force on the start date are previewed from the plan data (never hardcoded).
 * A custom number of days or a start in the past is a SuperAdmin override (D-081): only offered with
 * `canOverride`, and recorded with an explicit total price and a reason. The API enforces the same rule.
 */
export function RecordPeriodForm({
  subscription,
  plans,
  canOverride = false,
}: {
  subscription: Subscription;
  plans: Plan[];
  canOverride?: boolean;
}) {
  const t = useTranslations('adminSubscriptions.panel');
  const tb = useTranslations('billing');
  const tv = useTranslations('validation');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const renew = subscription.exists;
  const usable = plans.filter((p) =>
    renew
      ? p.status === 'Published' || p.status === 'Inactive'
      : p.status === 'Published' && p.availableToNewShops,
  );
  const [planId, setPlanId] = useState(
    (renew && usable.some((p) => p.id === subscription.planId) ? subscription.planId : usable[0]?.id) ?? '',
  );
  const [startDate, setStartDate] = useState<LocalDate>(subscription.nextStart);
  // After a renewal the page refreshes with a later default start; follow it (keeping the success message).
  const [defaultStart, setDefaultStart] = useState(subscription.nextStart);
  if (defaultStart !== subscription.nextStart) {
    setDefaultStart(subscription.nextStart);
    setStartDate(subscription.nextStart);
  }
  const [mode, setMode] = useState<'plan' | 'days'>('plan');
  const [days, setDays] = useState('30');
  const [notes, setNotes] = useState('');
  const [total, setTotal] = useState('');
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState<string>();
  const { errors, failure, setErrors, reset, handle } = useFieldErrors();

  const plan = usable.find((p) => p.id === planId);
  if (usable.length === 0) return <InlineAlert tone="warning" title={t('noPlans')} />;

  const dayCount = Number(days);
  const validDays = Number.isInteger(dayCount) && dayCount >= 1 && dayCount <= 1095;
  const end =
    plan && /^\d{4}-\d{2}-\d{2}$/.test(startDate)
      ? mode === 'plan'
        ? periodEnd(startDate, plan.intervalUnit, plan.intervalCount)
        : validDays
          ? periodEnd(startDate, 'Day', dayCount)
          : undefined
      : undefined;
  const price = plan && startDate ? priceOn(plan.prices, startDate) : undefined;
  const backdated = /^\d{4}-\d{2}-\d{2}$/.test(startDate) && startDate < subscription.today;
  const custom = mode === 'days' || backdated;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    reset();
    setDone(undefined);
    const next: FieldErrors = {};
    if (mode === 'days' && !validDays) next.durationDays = tv('outOfRange');
    if (custom && !canOverride) next.startDate = t('customNotAllowed');
    if (custom && canOverride) {
      if (parsePrice(total) === null) next.price = tv(total.trim() === '' ? 'required' : 'priceInvalid');
      if (reason.trim().length < 5) next.reason = tv('reasonRequired');
    }
    if (Object.keys(next).length > 0) {
      setErrors(next);
      return;
    }
    setPending(true);
    try {
      const body = {
        planId,
        startDate,
        durationDays: mode === 'days' ? dayCount : null,
        notes: notes.trim() || null,
        price: custom ? parsePrice(total) : null,
        reason: custom ? reason.trim() : null,
      };
      const result = renew
        ? ensureOk(
            await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/renew', {
              params: { path: { shopId: subscription.shopId } },
              body: { ...body, version: subscription.version ?? 0 },
            }),
          )
        : ensureOk(
            await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/assign', {
              params: { path: { shopId: subscription.shopId } },
              body,
            }),
          );
      setNotes('');
      setTotal('');
      setReason('');
      setDone(t(renew ? 'renewed' : 'assigned', { date: longDate(result.endDate ?? startDate, locale) }));
      router.refresh();
    } catch (error) {
      handle(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <form
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-4"
      data-testid="record-period-form"
    >
      <p className="text-caption text-text-secondary">{renew ? t('renewBody') : t('assignBody')}</p>
      <div className="grid gap-4 md:grid-cols-2">
        <SelectField
          label={t('planLabel')}
          value={planId}
          onChange={(e) => setPlanId(e.target.value)}
          error={errors.planId}
        >
          {usable.map((p) => (
            <option key={p.id} value={p.id}>
              {localizedName(locale, p.nameAr, p.nameEn)}
            </option>
          ))}
        </SelectField>
        <TextField
          label={t('startDate')}
          type="date"
          value={startDate}
          min={canOverride ? undefined : subscription.today}
          onChange={(e) => setStartDate(e.target.value)}
          dir="ltr"
          error={errors.startDate}
        />
      </div>
      <div className="flex flex-col gap-2">
        <span className="text-label font-bold text-text-primary">{t('duration')}</span>
        <SegmentedControl
          legend={t('duration')}
          name="duration-mode"
          value={mode}
          onValueChange={(value) => setMode(value as 'plan' | 'days')}
          options={[
            {
              value: 'plan',
              label: t('durationPlan', {
                interval: plan ? tb(plan.intervalUnit, { count: plan.intervalCount }) : '—',
              }),
            },
            ...(canOverride ? [{ value: 'days', label: t('durationDays') }] : []),
          ]}
        />
        {mode === 'days' && (
          <TextField
            label={t('days')}
            value={days}
            onChange={(e) => setDays(e.target.value)}
            dir="ltr"
            inputMode="numeric"
            error={errors.durationDays}
            className="max-w-[200px]"
          />
        )}
      </div>
      <div
        className="flex flex-col gap-1 rounded-button bg-bg-subtle p-3 text-caption text-text-primary"
        aria-live="polite"
      >
        {end && <span>{t('endPreview', { date: longDate(end, locale) })}</span>}
        {custom ? (
          <span className="font-semibold">
            {price
              ? t('customPlanPrice', { price: formatPrice(price.amount, locale, price.currency) })
              : t('noPricePreview')}
          </span>
        ) : price ? (
          <span className="font-semibold">
            {t('pricePreview', {
              price: formatPrice(price.amount, locale, price.currency),
              version: price.versionNumber,
            })}
          </span>
        ) : (
          <span className="text-warning-700">{t('noPricePreview')}</span>
        )}
      </div>
      {custom && canOverride && (
        <fieldset
          className="flex flex-col gap-3 rounded-button border border-warning-500 p-4"
          data-testid="custom-pricing"
        >
          <legend className="px-1 text-label font-bold text-text-primary">{t('customTitle')}</legend>
          <p className="text-caption text-text-secondary">{t('customBody')}</p>
          <div className="grid gap-3 md:grid-cols-2">
            <TextField
              label={t('customTotal')}
              value={total}
              onChange={(e) => setTotal(e.target.value)}
              dir="ltr"
              inputMode="decimal"
              autoComplete="off"
              error={errors.price}
            />
          </div>
          <TextareaField
            label={t('customReason')}
            value={reason}
            maxLength={500}
            onChange={(e) => setReason(e.target.value)}
            error={errors.reason}
          />
        </fieldset>
      )}
      <TextareaField
        label={t('notes')}
        helper={t('notesHelper')}
        optional
        value={notes}
        maxLength={500}
        onChange={(e) => setNotes(e.target.value)}
        error={errors.notes}
      />
      {failure && <InlineAlert tone="danger" title={failure} />}
      {done && <InlineAlert tone="success" title={done} />}
      <Button type="submit" size="md" loading={pending} disabled={!plan} className="self-start">
        {renew ? t('renew') : t('assign')}
      </Button>
    </form>
  );
}

/** Suspend (reason required) or reinstate; version-checked by the API. */
export function SuspensionControl({ subscription }: { subscription: Subscription }) {
  const t = useTranslations('adminSubscriptions.panel');
  const tv = useTranslations('validation');
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState<string>();
  const { errors, failure, setErrors, reset, handle } = useFieldErrors();

  const send = async (suspend: boolean) => {
    reset();
    setDone(undefined);
    if (suspend && reason.trim().length < 5) {
      setErrors({ reason: tv('reasonRequired') });
      return;
    }
    setPending(true);
    try {
      const path = { params: { path: { shopId: subscription.shopId } } };
      const version = subscription.version ?? 0;
      ensureOk(
        suspend
          ? await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/suspend', {
              ...path,
              body: { reason: reason.trim(), version },
            })
          : await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/reinstate', {
              ...path,
              body: { version },
            }),
      );
      setOpen(false);
      setReason('');
      setDone(suspend ? t('suspended') : t('reinstated'));
      router.refresh();
    } catch (error) {
      handle(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <p className="text-caption text-text-secondary">{t('suspendBody')}</p>
      {subscription.isSuspended ? (
        <Button
          size="sm"
          variant="secondary"
          icon="check"
          loading={pending}
          className="self-start"
          onClick={() => void send(false)}
        >
          {t('reinstate')}
        </Button>
      ) : (
        <Button size="sm" variant="danger" icon="eyeOff" className="self-start" onClick={() => setOpen(true)}>
          {t('suspend')}
        </Button>
      )}
      {failure && !open && <InlineAlert tone="danger" title={failure} />}
      {done && <InlineAlert tone="success" title={done} />}
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title={t('suspend')}
        description={t('suspendBody')}
        footer={
          <Button size="md" variant="dangerSolid" loading={pending} onClick={() => void send(true)}>
            {t('suspend')}
          </Button>
        }
      >
        <TextareaField
          label={t('reason')}
          value={reason}
          maxLength={500}
          error={errors.reason}
          onChange={(e) => setReason(e.target.value)}
        />
        {failure && <InlineAlert tone="danger" title={failure} />}
      </Dialog>
    </div>
  );
}

/** SuperAdmin override of the period in force: a price and/or an end date, with a reason (audited, kept in history). */
export function OverrideForm({ subscription }: { subscription: Subscription }) {
  const t = useTranslations('adminSubscriptions.panel');
  const tv = useTranslations('validation');
  const router = useRouter();
  const [price, setPrice] = useState('');
  const [endDate, setEndDate] = useState('');
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState<string>();
  const { errors, failure, setErrors, reset, handle } = useFieldErrors();

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    reset();
    setDone(undefined);
    const amount = price.trim() === '' ? null : parsePrice(price);
    const next: FieldErrors = {};
    if (price.trim() !== '' && amount === null) next.price = tv('priceInvalid');
    if (amount === null && endDate === '') next.price = tv('required');
    if (reason.trim().length < 5) next.reason = tv('reasonRequired');
    if (Object.keys(next).length > 0) {
      setErrors(next);
      return;
    }
    setPending(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/admin/shops/{shopId}/subscription/override', {
          params: { path: { shopId: subscription.shopId } },
          body: {
            price: amount,
            endDate: endDate || null,
            reason: reason.trim(),
            version: subscription.version ?? 0,
          },
        }),
      );
      setPrice('');
      setEndDate('');
      setReason('');
      setDone(t('overridden'));
      router.refresh();
    } catch (error) {
      handle(error);
    } finally {
      setPending(false);
    }
  };

  return (
    <form
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-3"
      data-testid="override-form"
    >
      <p className="text-caption text-text-secondary">{t('overrideBody')}</p>
      <div className="grid gap-3 md:grid-cols-2">
        <TextField
          label={t('overridePrice')}
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          dir="ltr"
          inputMode="decimal"
          optional
          error={errors.price}
        />
        <TextField
          label={t('overrideEnd')}
          type="date"
          value={endDate}
          onChange={(e) => setEndDate(e.target.value)}
          dir="ltr"
          optional
          error={errors.endDate}
        />
      </div>
      <TextareaField
        label={t('overrideReason')}
        value={reason}
        maxLength={500}
        onChange={(e) => setReason(e.target.value)}
        error={errors.reason}
      />
      {failure && <InlineAlert tone="danger" title={failure} />}
      {done && <InlineAlert tone="success" title={done} />}
      <Button type="submit" size="md" loading={pending} className="self-start">
        {t('overrideSubmit')}
      </Button>
    </form>
  );
}
