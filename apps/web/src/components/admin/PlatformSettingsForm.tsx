'use client';

import { useTranslations } from 'next-intl';
import { type ReactNode, useId, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, Switch, TextField } from '@/components/ui/inputs';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { codeToMessageKey } from '@/lib/forms/problem';

type Settings = components['schemas']['PlatformSettingsResponse'];
type Enforcement = components['schemas']['SubscriptionEnforcement'];

const NUMBER_FIELDS = [
  'minLeadTimeMinutes',
  'bookingHorizonDays',
  'cancellationCutoffMinutes',
  'reviewWindowDays',
  'reminderOffsetMinutes',
  'expiringSoonThresholdDays',
  'mapDefaultLatitude',
  'mapDefaultLongitude',
  'mapDefaultZoom',
] as const;
type NumberField = (typeof NUMBER_FIELDS)[number];

const SLOT_STEPS = [5, 10, 15, 20, 30, 60];
const ENFORCEMENTS: Enforcement[] = ['HideAndBlockNewOnlineBookings', 'None'];

function Section({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return (
    <section
      aria-labelledby={id}
      className="flex flex-col gap-4 rounded-card border border-border bg-surface p-5 shadow-e1"
    >
      <h2 id={id} className="text-h3 font-bold text-navy-900">
        {title}
      </h2>
      <div className="grid gap-4 md:grid-cols-2">{children}</div>
    </section>
  );
}

/**
 * The platform settings that later phases read (D-076): booking policy, reminder offset, subscription threshold and
 * enforcement (D-014), discovery (D-013) and map defaults. Region values are shown read-only in v1. Ranges are
 * validated by the API (field errors come back per field); a stale version answers 409.
 */
export function PlatformSettingsForm({ settings, canEdit }: { settings: Settings; canEdit: boolean }) {
  const t = useTranslations('platformSettings');
  const tv = useTranslations('validation');
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [values, setValues] = useState<Record<NumberField, string>>(
    () =>
      Object.fromEntries(NUMBER_FIELDS.map((f) => [f, String(settings[f])])) as Record<NumberField, string>,
  );
  const [slotStep, setSlotStep] = useState(String(settings.slotStepMinutes));
  const [enforcement, setEnforcement] = useState<Enforcement>(settings.expiredSubscriptionEnforcement);
  const [hidePaused, setHidePaused] = useState(settings.hidePausedShopsFromDiscovery);
  const [errors, setErrors] = useState<Partial<Record<string, string>>>({});
  const [failure, setFailure] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [pending, setPending] = useState(false);

  const numberField = (field: NumberField, decimal = false) => (
    <TextField
      key={field}
      label={t(`fields.${field}`)}
      value={values[field]}
      onChange={(e) => setValues((current) => ({ ...current, [field]: e.target.value }))}
      dir="ltr"
      inputMode={decimal ? 'decimal' : 'numeric'}
      autoComplete="off"
      readOnly={!canEdit}
      error={errors[field]}
    />
  );

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaved(false);
    setFailure(undefined);
    const invalid = NUMBER_FIELDS.filter((f) => values[f].trim() === '' || Number.isNaN(Number(values[f])));
    if (invalid.length > 0) {
      setErrors(Object.fromEntries(invalid.map((f) => [f, tv('invalid')])));
      return;
    }
    setErrors({});
    setPending(true);
    try {
      ensureOk(
        await browserApi.PUT('/api/v1/admin/settings', {
          body: {
            ...(Object.fromEntries(NUMBER_FIELDS.map((f) => [f, Number(values[f])])) as Record<
              NumberField,
              number
            >),
            slotStepMinutes: Number(slotStep),
            expiredSubscriptionEnforcement: enforcement,
            hidePausedShopsFromDiscovery: hidePaused,
            version: settings.version,
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
      noValidate
      onSubmit={(e) => void submit(e)}
      className="flex flex-col gap-5"
      data-testid="platform-settings-form"
    >
      {!canEdit && <InlineAlert tone="info" title={t('readOnly')} />}
      <Section title={t('sections.booking')}>
        {numberField('minLeadTimeMinutes')}
        {numberField('bookingHorizonDays')}
        <SelectField
          label={t('fields.slotStepMinutes')}
          value={slotStep}
          disabled={!canEdit}
          onChange={(e) => setSlotStep(e.target.value)}
          error={errors.slotStepMinutes}
        >
          {SLOT_STEPS.map((step) => (
            <option key={step} value={step}>
              {step}
            </option>
          ))}
        </SelectField>
        {numberField('cancellationCutoffMinutes')}
        {numberField('reviewWindowDays')}
      </Section>
      <Section title={t('sections.notifications')}>{numberField('reminderOffsetMinutes')}</Section>
      <Section title={t('sections.subscriptions')}>
        {numberField('expiringSoonThresholdDays')}
        <SelectField
          label={t('fields.expiredSubscriptionEnforcement')}
          helper={t('enforcementHelper')}
          value={enforcement}
          disabled={!canEdit}
          onChange={(e) => setEnforcement(e.target.value as Enforcement)}
        >
          {ENFORCEMENTS.map((value) => (
            <option key={value} value={value}>
              {t(`enforcement.${value}`)}
            </option>
          ))}
        </SelectField>
      </Section>
      <Section title={t('sections.discovery')}>
        <Switch
          checked={hidePaused}
          disabled={!canEdit}
          onCheckedChange={setHidePaused}
          label={t('fields.hidePausedShopsFromDiscovery')}
        />
      </Section>
      <Section title={t('sections.map')}>
        {numberField('mapDefaultLatitude', true)}
        {numberField('mapDefaultLongitude', true)}
        {numberField('mapDefaultZoom')}
      </Section>
      <Section title={t('sections.regional')}>
        {(['defaultLocale', 'currency', 'timeZone', 'countryCode'] as const).map((field) => (
          <TextField key={field} label={t(`fields.${field}`)} value={settings[field]} readOnly dir="ltr" />
        ))}
      </Section>
      {failure && <InlineAlert tone="danger" title={failure} />}
      {saved && <InlineAlert tone="success" title={t('saved')} />}
      {canEdit && (
        <Button type="submit" size="md" loading={pending} className="self-start">
          {t('save')}
        </Button>
      )}
    </form>
  );
}
