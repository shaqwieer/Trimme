'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, TextField } from '@/components/ui/inputs';
import { NumberStepper } from '@/components/ui/NumberStepper';
import { toLatinDigits } from '@/components/ui/digits';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { codeToMessageKey } from '@/lib/forms/problem';
import { type AppLocale, formatNumber, formatPrice } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { LoyaltyRulesText } from './LoyaltyParts';

type Rules = components['schemas']['LoyaltyRulesResponse'];
type RewardType = components['schemas']['LoyaltyRewardType'];
type InactivityRule = components['schemas']['LoyaltyInactivityRule'];

/** Whose rules: the owner's own shop (from the claims) or, for the admin, one shop. */
export type LoyaltyRulesScope = { kind: 'shop' } | { kind: 'admin'; shopId: string };

/** One of the shop's services, for the free-service reward. */
export type LoyaltyRulesService = { id: string; nameAr: string; nameEn: string | null };

/** The API's ranges (`LoyaltySettings`, D-149), checked here first so a value outside them is caught before saving. */
export const LOYALTY_RANGES = {
  firstVisitPoints: [1, 100],
  visitPoints: [1, 100],
  pointsForReward: [2, 1000],
  rewardAmount: [5, 1000],
  inactivityDays: [7, 730],
} as const satisfies Record<string, readonly [number, number]>;

const REWARD_TYPES: RewardType[] = ['FreeService', 'AmountOff'];
/** What happens once the period passes without a visit (the app's «تنتهي النقاط … وبعدها»). */
const AFTER_RULES: Exclude<InactivityRule, 'None'>[] = ['Wipe', 'DecayPerPeriod'];
/** The app's ready periods (D-151): a month, two and three; any other length stays «مدة أخرى». */
const PERIODS = [30, 60, 90] as const;
type Period = 'none' | (typeof PERIODS)[number] | 'custom';
const PERIOD_KEYS: Record<(typeof PERIODS)[number], 'month' | 'twoMonths' | 'threeMonths'> = {
  30: 'month',
  60: 'twoMonths',
  90: 'threeMonths',
};

function periodOf(rules: Rules | null): Period {
  if (!rules || rules.inactivityRule === 'None' || rules.inactivityDays == null) return 'none';
  return (PERIODS as readonly number[]).includes(rules.inactivityDays)
    ? (rules.inactivityDays as (typeof PERIODS)[number])
    : 'custom';
}

type Field = keyof typeof LOYALTY_RANGES | 'rewardServiceId';

/** Typed numbers in Latin or Arabic-Indic digits (an Arabic iPhone keypad types «٣»), with «٫» or «,» as the point. */
const normalized = (value: string) => toLatinDigits(value.trim()).replace(/[٫,]/g, '.');
const isWhole = (value: string) => /^\d+$/.test(normalized(value));
const inRange = (value: number, [min, max]: readonly [number, number]) => value >= min && value <= max;

/**
 * The shop's loyalty rules (D-149, the client's 6-2): points for the first visit and for each later one, the points a
 * reward needs, the reward (one of the shop's services free, or a fixed 5–1,000 SAR off) and what happens when a customer
 * stops visiting (nothing, the card wiped, or one point off per period). Set by the owner (`Shop.Profile.Edit`; barbers
 * read) or by the admin on the shop's page. Customers collect and use points only while the paid add-on is on; the rules
 * can be set before. A preview shows the rules as customers read them. A stale version answers 409.
 */
export function LoyaltyRulesForm({
  scope,
  rules,
  version,
  addOnActive,
  services,
  canEdit,
  subscriptionHref,
}: {
  scope: LoyaltyRulesScope;
  rules: Rules | null;
  version: number | null;
  addOnActive: boolean;
  services: LoyaltyRulesService[];
  canEdit: boolean;
  /** The admin's subscription tab, where the paid add-on is switched on for a period (D-150). */
  subscriptionHref?: string;
}) {
  const t = useTranslations('loyalty.rulesForm');
  const tv = useTranslations('validation');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [first, setFirst] = useState(String(rules?.firstVisitPoints ?? 1));
  const [visit, setVisit] = useState(String(rules?.visitPoints ?? 1));
  const [needed, setNeeded] = useState(String(rules?.pointsForReward ?? 6));
  const [rewardType, setRewardType] = useState<RewardType>(rules?.rewardType ?? 'FreeService');
  const [serviceId, setServiceId] = useState(rules?.rewardServiceId ?? '');
  const [amount, setAmount] = useState(rules?.rewardAmount != null ? String(rules.rewardAmount) : '');
  const [period, setPeriod] = useState<Period>(periodOf(rules));
  const [afterRule, setAfterRule] = useState<Exclude<InactivityRule, 'None'>>(
    rules?.inactivityRule === 'DecayPerPeriod' ? 'DecayPerPeriod' : 'Wipe',
  );
  // «مدة أخرى»: the days typed (or the saved length that is not one of the ready periods).
  const [days, setDays] = useState(rules?.inactivityDays != null ? String(rules.inactivityDays) : '120');
  const inactivity: InactivityRule = period === 'none' ? 'None' : afterRule;
  const effectiveDays = period === 'none' ? null : period === 'custom' ? days : String(period);
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({});
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [pending, setPending] = useState(false);

  const range = ([min, max]: readonly [number, number]) =>
    t('range', { min: formatNumber(min, locale), max: formatNumber(max, locale) });
  // The saved free service stays choosable when the list lacks it (archived since, or a barber's read-only view).
  const options: LoyaltyRulesService[] =
    rules?.rewardServiceId && !services.some((s) => s.id === rules.rewardServiceId)
      ? [
          ...services,
          {
            id: rules.rewardServiceId,
            nameAr: rules.rewardServiceNameAr ?? '',
            nameEn: rules.rewardServiceNameEn,
          },
        ]
      : services;
  const service = options.find((s) => s.id === serviceId);
  const parsedAmount = Number(normalized(amount));

  /** What is entered, checked against the API's ranges; null fields are the ones that are not valid. */
  const check = () => {
    const local: Partial<Record<Field, string>> = {};
    // The message names the range (the client's 3-7: «هذه القيمة خارج النطاق المسموح» didn't say why).
    const outside = ([min, max]: readonly [number, number], money = false) =>
      t('outOfRange', {
        min: money ? formatPrice(min, locale) : formatNumber(min, locale),
        max: money ? formatPrice(max, locale) : formatNumber(max, locale),
      });
    const whole = (field: keyof typeof LOYALTY_RANGES, value: string) => {
      if (!isWhole(value) || !inRange(Number(normalized(value)), LOYALTY_RANGES[field]))
        local[field] = outside(LOYALTY_RANGES[field]);
    };
    whole('firstVisitPoints', first);
    whole('visitPoints', visit);
    whole('pointsForReward', needed);
    if (rewardType === 'FreeService' && !service) local.rewardServiceId = tv('required');
    if (
      rewardType === 'AmountOff' &&
      (!/^\d+(\.\d{1,2})?$/.test(normalized(amount)) || !inRange(parsedAmount, LOYALTY_RANGES.rewardAmount))
    )
      local.rewardAmount = outside(LOYALTY_RANGES.rewardAmount, true);
    if (effectiveDays !== null) whole('inactivityDays', effectiveDays);
    return local;
  };

  const preview: Rules | null =
    Object.keys(check()).length === 0
      ? {
          firstVisitPoints: Number(first),
          visitPoints: Number(visit),
          pointsForReward: Number(needed),
          rewardType,
          rewardServiceId: rewardType === 'FreeService' ? serviceId : null,
          rewardServiceNameAr: rewardType === 'FreeService' ? (service?.nameAr ?? null) : null,
          rewardServiceNameEn: rewardType === 'FreeService' ? (service?.nameEn ?? null) : null,
          rewardAmount: rewardType === 'AmountOff' ? parsedAmount : null,
          inactivityRule: inactivity,
          inactivityDays: effectiveDays === null ? null : Number(normalized(effectiveDays)),
        }
      : null;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSaved(false);
    setFailure(undefined);
    const local = check();
    setErrors(local);
    if (Object.keys(local).length > 0 || !preview) return;

    const body = {
      firstVisitPoints: preview.firstVisitPoints,
      visitPoints: preview.visitPoints,
      pointsForReward: preview.pointsForReward,
      rewardType: preview.rewardType,
      rewardServiceId: preview.rewardServiceId,
      rewardAmount: preview.rewardAmount,
      inactivityRule: preview.inactivityRule,
      inactivityDays: preview.inactivityDays,
      version,
    };
    setPending(true);
    try {
      ensureOk(
        scope.kind === 'shop'
          ? await browserApi.PUT('/api/v1/shop/loyalty', { body })
          : await browserApi.PUT('/api/v1/admin/shops/{shopId}/loyalty', {
              params: { path: { shopId: scope.shopId } },
              body,
            }),
      );
      setSaved(true);
      router.refresh();
    } catch (error) {
      if (error instanceof ApiError && error.isValidation) {
        setErrors(
          Object.fromEntries(
            Object.entries(error.fieldErrors).map(([field, codes]) => [
              field,
              tv(codeToMessageKey(codes[0]) as 'generic'),
            ]),
          ),
        );
      } else setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
    } finally {
      setPending(false);
    }
  };

  const number = (
    field: keyof typeof LOYALTY_RANGES,
    value: string,
    onChange: (value: string) => void,
    label: string,
    helper: string,
    inputMode: 'numeric' | 'decimal' = 'numeric',
  ) => (
    <TextField
      label={label}
      name={field}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      dir="ltr"
      inputMode={inputMode}
      autoComplete="off"
      readOnly={!canEdit}
      helper={helper}
      error={errors[field]}
    />
  );

  const stepper = (
    field: 'firstVisitPoints' | 'visitPoints' | 'pointsForReward',
    value: string,
    onChange: (value: string) => void,
    label: string,
  ) => {
    const [min, max] = LOYALTY_RANGES[field];
    const current = isWhole(value) ? Math.min(max, Math.max(min, Number(normalized(value)))) : min;
    return (
      <div className="flex flex-col gap-1 py-3">
        <NumberStepper
          label={label}
          value={current}
          min={min}
          max={max}
          format={(n) => formatNumber(n, locale)}
          disabled={!canEdit}
          onChange={(n) => onChange(String(n))}
          testId={`loyalty-${field}`}
        />
        <span className="text-helper text-text-tertiary">{range(LOYALTY_RANGES[field])}</span>
        {errors[field] && <span className="text-helper text-danger-700">{errors[field]}</span>}
      </div>
    );
  };

  return (
    <form
      method="post"
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-5"
      data-testid="loyalty-rules-form"
    >
      <InlineAlert
        tone={addOnActive ? 'success' : 'info'}
        title={addOnActive ? t('addOnOn') : t(scope.kind === 'admin' ? 'addOnOffAdmin' : 'addOnOff')}
      >
        {/* «ميزة مدفوعة»: the shop cannot switch it on itself (D-150, the client's 5-2). */}
        {scope.kind === 'admin' && (
          <p>
            {t('paidAdmin')}{' '}
            {subscriptionHref && (
              <Link href={subscriptionHref} className="font-bold text-text-link underline">
                {t('toSubscription')}
              </Link>
            )}
          </p>
        )}
      </InlineAlert>
      {!canEdit && (
        <InlineAlert tone="info" title={t(scope.kind === 'admin' ? 'readOnlyAdmin' : 'readOnly')} />
      )}
      {!rules && <InlineAlert tone="info" title={t('notSet')} />}
      <p className="text-caption text-text-secondary">{t('intro')}</p>

      {/* − and + as in the app (D-151); the ranges are the API's. */}
      <div className="flex flex-col divide-y divide-border-row rounded-card border border-border px-4">
        {stepper('firstVisitPoints', first, setFirst, t('firstVisitPoints'))}
        {stepper('visitPoints', visit, setVisit, t('visitPoints'))}
        {stepper('pointsForReward', needed, setNeeded, t('pointsForReward'))}
      </div>

      <fieldset className="flex min-w-0 flex-col gap-2" disabled={!canEdit}>
        <legend className="mb-1 text-label font-bold text-text-strong">{t('reward')}</legend>
        {REWARD_TYPES.map((option) => (
          <RadioCard
            key={option}
            name="rewardType"
            value={option}
            checked={rewardType === option}
            onChange={() => setRewardType(option)}
          >
            {t(`rewardTypes.${option}`)}
          </RadioCard>
        ))}
      </fieldset>
      {rewardType === 'FreeService' ? (
        <SelectField
          label={t('rewardService')}
          name="rewardServiceId"
          value={serviceId}
          onChange={(e) => setServiceId(e.target.value)}
          disabled={!canEdit}
          error={errors.rewardServiceId}
        >
          <option value="">{t('chooseService')}</option>
          {options.map((s) => (
            <option key={s.id} value={s.id}>
              {localizedName(locale, s.nameAr, s.nameEn)}
            </option>
          ))}
        </SelectField>
      ) : (
        number(
          'rewardAmount',
          amount,
          setAmount,
          t('rewardAmount'),
          t('range', {
            min: formatPrice(LOYALTY_RANGES.rewardAmount[0], locale),
            max: formatPrice(LOYALTY_RANGES.rewardAmount[1], locale),
          }),
          'decimal',
        )
      )}

      <fieldset className="flex min-w-0 flex-col gap-2" disabled={!canEdit} data-testid="loyalty-expiry">
        <legend className="mb-1 text-label font-bold text-text-strong">{t('expiry.legend')}</legend>
        <div className="grid grid-cols-2 gap-2 md:grid-cols-5">
          {(['none', ...PERIODS, 'custom'] as const).map((option) => (
            <RadioCard
              key={option}
              name="inactivityPeriod"
              value={String(option)}
              checked={period === option}
              onChange={() => setPeriod(option)}
            >
              {typeof option === 'number' ? t(`expiry.${PERIOD_KEYS[option]}`) : t(`expiry.${option}`)}
            </RadioCard>
          ))}
        </div>
      </fieldset>
      {period === 'custom' &&
        number(
          'inactivityDays',
          days,
          setDays,
          t('inactivityDays'),
          t('inactivityHelper', {
            min: formatNumber(LOYALTY_RANGES.inactivityDays[0], locale),
            max: formatNumber(LOYALTY_RANGES.inactivityDays[1], locale),
          }),
        )}
      {period !== 'none' && (
        <fieldset className="flex min-w-0 flex-col gap-2" disabled={!canEdit}>
          <legend className="mb-1 text-label font-bold text-text-strong">{t('expiry.then')}</legend>
          {AFTER_RULES.map((option) => (
            <RadioCard
              key={option}
              name="inactivityRule"
              value={option}
              checked={afterRule === option}
              onChange={() => setAfterRule(option)}
            >
              {t(`expiry.${option}`)}
            </RadioCard>
          ))}
        </fieldset>
      )}

      {preview && (
        <section
          className="flex flex-col gap-2 rounded-card border border-success-500/40 bg-success-50 p-4"
          aria-label={t('preview')}
          data-testid="loyalty-rules-preview"
        >
          <p className="text-helper font-bold text-success-700">{t('preview')}</p>
          <LoyaltyRulesText rules={preview} />
        </section>
      )}

      {failure && <InlineAlert tone="danger" title={failure} />}
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {canEdit && (
        <Button type="submit" variant="primary" icon="check" loading={pending} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
