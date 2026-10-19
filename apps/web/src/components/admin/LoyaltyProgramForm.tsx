'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { Switch, TextField } from '@/components/ui/inputs';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { codeToMessageKey } from '@/lib/forms/problem';
import { type AppLocale, formatNumber } from '@/lib/i18n/format';

type Loyalty = components['schemas']['AdminShopLoyaltyResponse'];
type RewardKind = components['schemas']['LoyaltyRewardKind'];

/** The API's ranges (D-132), checked here first so a value outside them is caught before saving. */
const VISITS: [number, number] = [2, 20];
const PERCENT: [number, number] = [1, 100];
const VALIDITY: [number, number] = [7, 365];
const KINDS: RewardKind[] = ['FreeService', 'Discount'];

type Field = 'requiredVisits' | 'discountPercent' | 'rewardTitleAr' | 'rewardTitleEn' | 'rewardValidityDays';

/**
 * A shop's loyalty terms (D-132), agreed between the platform and the shop: on or off, how many completed visits fill
 * the card (with any of the shop's professionals), and the reward. Saving replaces the terms for the next completed
 * visits; earned coupons keep theirs. A stale version answers 409.
 */
export function LoyaltyProgramForm({
  shopId,
  loyalty,
  canEdit,
}: {
  shopId: string;
  loyalty: Loyalty;
  canEdit: boolean;
}) {
  const t = useTranslations('loyalty.admin');
  const tv = useTranslations('validation');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const program = loyalty.program;
  const [active, setActive] = useState(program?.isActive ?? true);
  const [visits, setVisits] = useState(String(program?.requiredVisits ?? 6));
  const [kind, setKind] = useState<RewardKind>(program?.rewardKind ?? 'FreeService');
  const [percent, setPercent] = useState(program?.discountPercent ? String(program.discountPercent) : '');
  const [titleAr, setTitleAr] = useState(program?.rewardTitleAr ?? '');
  const [titleEn, setTitleEn] = useState(program?.rewardTitleEn ?? '');
  const [validity, setValidity] = useState(
    program?.rewardValidityDays ? String(program.rewardValidityDays) : '',
  );
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({});
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [pending, setPending] = useState(false);

  const range = ([min, max]: [number, number]) =>
    t('range', { min: formatNumber(min, locale), max: formatNumber(max, locale) });
  const outside = (value: string, [min, max]: [number, number]) => {
    const number = Number(value);
    return value.trim() === '' || !Number.isInteger(number) || number < min || number > max;
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaved(false);
    setFailure(undefined);
    const local: Partial<Record<Field, string>> = {};
    if (outside(visits, VISITS)) local.requiredVisits = tv('outOfRange');
    if (kind === 'Discount' && outside(percent, PERCENT)) local.discountPercent = tv('outOfRange');
    if (titleAr.trim() === '') local.rewardTitleAr = tv('required');
    if (validity.trim() !== '' && outside(validity, VALIDITY)) local.rewardValidityDays = tv('outOfRange');
    setErrors(local);
    if (Object.keys(local).length > 0) return;

    setPending(true);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/shops/{shopId}/loyalty', {
          params: { path: { shopId } },
          body: {
            isActive: active,
            requiredVisits: Number(visits),
            rewardKind: kind,
            discountPercent: kind === 'Discount' ? Number(percent) : null,
            rewardTitleAr: titleAr.trim(),
            rewardTitleEn: titleEn.trim() || null,
            rewardValidityDays: validity.trim() === '' ? null : Number(validity),
            version: loyalty.version ?? null,
          },
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

  return (
    <form
      method="post"
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-5"
      data-testid="loyalty-program-form"
    >
      {!canEdit && <InlineAlert tone="info" title={t('readOnly')} />}
      {!program && <InlineAlert tone="info" title={t('notSet')} />}
      <Switch checked={active} onCheckedChange={setActive} label={t('active')} disabled={!canEdit} />
      <TextField
        label={t('requiredVisits')}
        value={visits}
        onChange={(e) => setVisits(e.target.value)}
        dir="ltr"
        inputMode="numeric"
        autoComplete="off"
        readOnly={!canEdit}
        helper={range(VISITS)}
        error={errors.requiredVisits}
      />
      <fieldset className="flex flex-col gap-2" disabled={!canEdit}>
        <legend className="mb-1 text-label font-bold text-text-strong">{t('rewardKind')}</legend>
        {KINDS.map((option) => (
          <RadioCard
            key={option}
            name="rewardKind"
            value={option}
            checked={kind === option}
            onChange={() => setKind(option)}
          >
            {t(`kinds.${option}`)}
          </RadioCard>
        ))}
      </fieldset>
      {kind === 'Discount' && (
        <TextField
          label={t('discountPercent')}
          value={percent}
          onChange={(e) => setPercent(e.target.value)}
          dir="ltr"
          inputMode="numeric"
          autoComplete="off"
          readOnly={!canEdit}
          helper={range(PERCENT)}
          error={errors.discountPercent}
        />
      )}
      <div className="grid gap-4 md:grid-cols-2">
        <TextField
          label={t('rewardTitleAr')}
          value={titleAr}
          onChange={(e) => setTitleAr(e.target.value)}
          dir="rtl"
          lang="ar"
          maxLength={60}
          readOnly={!canEdit}
          helper={t('rewardTitleArHint')}
          error={errors.rewardTitleAr}
        />
        <TextField
          label={t('rewardTitleEn')}
          value={titleEn}
          onChange={(e) => setTitleEn(e.target.value)}
          dir="ltr"
          lang="en"
          maxLength={60}
          readOnly={!canEdit}
          error={errors.rewardTitleEn}
        />
      </div>
      <TextField
        label={t('validityDays')}
        value={validity}
        onChange={(e) => setValidity(e.target.value)}
        dir="ltr"
        inputMode="numeric"
        autoComplete="off"
        readOnly={!canEdit}
        helper={t('validityHint', {
          min: formatNumber(VALIDITY[0], locale),
          max: formatNumber(VALIDITY[1], locale),
        })}
        error={errors.rewardValidityDays}
      />
      {failure && <InlineAlert tone="danger" title={failure} />}
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {canEdit && (
        <Button type="submit" variant="primary" loading={pending} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
