'use client';

import { useLocale, useTranslations } from 'next-intl';
import { type FormEvent, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, Switch } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { formatDurationMinutes } from '@/lib/i18n/format';

type Settings = components['schemas']['ShopBookingSettingsResponse'];
type Field = Exclude<keyof Settings, 'requireManualConfirmation'>;

/** Whose settings: the owner's own shop (from the claims) or, for the admin, one shop. */
export type BookingSettingsScope = { kind: 'shop' } | { kind: 'admin'; shopId: string };

/** The choices offered, inside the API's bounds (`ShopBookingRules`): starts, notice, furthest day, cleaning. */
const CHOICES: Record<Field, number[]> = {
  slotStepMinutes: [5, 10, 15, 20, 30, 60],
  minLeadTimeMinutes: [0, 15, 30, 60, 120, 180, 360, 720, 1440],
  bookingHorizonDays: [7, 14, 21, 30, 45, 60, 90],
  bufferMinutes: [0, 5, 10, 15, 20, 30, 45, 60],
};
const FIELDS = Object.keys(CHOICES) as Field[];

/** A value set elsewhere (the platform's default, say) that is not one of the choices stays choosable. */
const choicesWith = (field: Field, current: number) =>
  CHOICES[field].includes(current) ? CHOICES[field] : [...CHOICES[field], current].sort((a, b) => a - b);

/**
 * A shop's own booking settings (D-149, the owner's item 5): whether online bookings wait for the shop's confirmation,
 * the start-time interval, the minimum notice, the furthest bookable day and the cleaning time after each booking. The
 * start interval is not a service's length: starts every 15 minutes, a service of 45. Set by the owner
 * (`Shop.Profile.Edit`) or by the admin on the shop's page; the values shown are the ones in force.
 */
export function BookingSettingsForm({
  scope,
  settings,
  canEdit,
}: {
  scope: BookingSettingsScope;
  settings: Settings;
  canEdit: boolean;
}) {
  const t = useTranslations('bookingSettings');
  const tv = useTranslations('validation');
  const locale = asLocale(useLocale());
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [manual, setManual] = useState(settings.requireManualConfirmation);
  const [values, setValues] = useState<Record<Field, number>>(
    () => Object.fromEntries(FIELDS.map((f) => [f, settings[f]])) as Record<Field, number>,
  );
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>({});
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [pending, setPending] = useState(false);

  const label = (field: Field, value: number): string => {
    if (field === 'slotStepMinutes')
      return t('everyMinutes', { duration: formatDurationMinutes(value, locale) });
    if (field === 'bookingHorizonDays') return t('days', { count: value });
    if (value === 0) return t(field === 'bufferMinutes' ? 'noCleaning' : 'noNotice');
    return formatDurationMinutes(value, locale);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setPending(true);
    setFailure(undefined);
    setErrors({});
    setSaved(false);
    const body = { requireManualConfirmation: manual, ...values };
    try {
      ensureOk(
        scope.kind === 'shop'
          ? await browserApi.PUT('/api/v1/shop/booking-settings', { body })
          : await browserApi.PUT('/api/v1/admin/shops/{shopId}/booking-settings', {
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
            Object.keys(error.fieldErrors)
              .filter((f): f is Field => (FIELDS as string[]).includes(f))
              .map((f) => [f, tv('outOfRange')]),
          ),
        );
      }
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
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
      data-testid="booking-settings"
    >
      <fieldset className="flex min-w-0 flex-col gap-1" disabled={!canEdit}>
        <legend className="sr-only">{t('confirmation')}</legend>
        <Switch checked={manual} onCheckedChange={setManual} label={t('manual')} disabled={!canEdit} />
        <span className="text-helper text-text-tertiary">{t(manual ? 'manualOn' : 'manualOff')}</span>
      </fieldset>
      <fieldset className="grid min-w-0 gap-4 md:grid-cols-2" disabled={!canEdit}>
        <legend className="sr-only">{t('times')}</legend>
        {FIELDS.map((field) => (
          <SelectField
            key={field}
            label={t(`fields.${field}`)}
            helper={t(`hints.${field}`)}
            name={field}
            value={String(values[field])}
            onChange={(e) => setValues((current) => ({ ...current, [field]: Number(e.target.value) }))}
            error={errors[field]}
          >
            {choicesWith(field, values[field]).map((value) => (
              <option key={value} value={value}>
                {label(field, value)}
              </option>
            ))}
          </SelectField>
        ))}
      </fieldset>
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {failure && <InlineAlert tone="danger" title={failure} />}
      {canEdit && (
        <Button type="submit" icon="check" loading={pending} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
