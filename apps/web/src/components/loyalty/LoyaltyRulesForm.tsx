'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, TextField } from '@/components/ui/inputs';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
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
const INACTIVITY_RULES: InactivityRule[] = ['None', 'Wipe', 'DecayPerPeriod'];

type Field = keyof typeof LOYALTY_RANGES | 'rewardServiceId';

const isWhole = (value: string) => /^\d+$/.test(value.trim());
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
}: {
  scope: LoyaltyRulesScope;
  rules: Rules | null;
  version: number | null;
  addOnActive: boolean;
  services: LoyaltyRulesService[];
  canEdit: boolean;
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
  const [inactivity, setInactivity] = useState<InactivityRule>(rules?.inactivityRule ?? 'None');
  const [days, setDays] = useState(rules?.inactivityDays != null ? String(rules.inactivityDays) : '90');
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
  const parsedAmount = Number(amount.replace(',', '.'));

  /** What is entered, checked against the API's ranges; null fields are the ones that are not valid. */
  const check = () => {
    const local: Partial<Record<Field, string>> = {};
    const whole = (field: keyof typeof LOYALTY_RANGES, value: string) => {
      if (!isWhole(value) || !inRange(Number(value), LOYALTY_RANGES[field])) local[field] = tv('outOfRange');
    };
    whole('firstVisitPoints', first);
    whole('visitPoints', visit);
    whole('pointsForReward', needed);
    if (rewardType === 'FreeService' && !service) local.rewardServiceId = tv('required');
    if (
      rewardType === 'AmountOff' &&
      (!/^\d+([.,]\d{1,2})?$/.test(amount.trim()) || !inRange(parsedAmount, LOYALTY_RANGES.rewardAmount))
    )
      local.rewardAmount = tv('outOfRange');
    if (inactivity !== 'None') whole('inactivityDays', days);
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
          inactivityDays: inactivity === 'None' ? null : Number(days),
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
      />
      {!canEdit && (
        <InlineAlert tone="info" title={t(scope.kind === 'admin' ? 'readOnlyAdmin' : 'readOnly')} />
      )}
      {!rules && <InlineAlert tone="info" title={t('notSet')} />}
      <p className="text-caption text-text-secondary">{t('intro')}</p>

      <div className="grid gap-4 md:grid-cols-3">
        {number(
          'firstVisitPoints',
          first,
          setFirst,
          t('firstVisitPoints'),
          range(LOYALTY_RANGES.firstVisitPoints),
        )}
        {number('visitPoints', visit, setVisit, t('visitPoints'), range(LOYALTY_RANGES.visitPoints))}
        {number(
          'pointsForReward',
          needed,
          setNeeded,
          t('pointsForReward'),
          range(LOYALTY_RANGES.pointsForReward),
        )}
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

      <fieldset className="flex min-w-0 flex-col gap-2" disabled={!canEdit}>
        <legend className="mb-1 text-label font-bold text-text-strong">{t('inactivity')}</legend>
        {INACTIVITY_RULES.map((option) => (
          <RadioCard
            key={option}
            name="inactivityRule"
            value={option}
            checked={inactivity === option}
            onChange={() => setInactivity(option)}
          >
            {t(`inactivityRules.${option}`)}
          </RadioCard>
        ))}
      </fieldset>
      {inactivity !== 'None' &&
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
