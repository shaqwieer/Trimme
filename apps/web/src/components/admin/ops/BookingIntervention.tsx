'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { SelectField, TextareaField } from '@/components/ui/inputs';
import { Dialog } from '@/components/ui/overlays';
import { InlineAlert } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

type Status = components['schemas']['BookingStatus'];
type Options = components['schemas']['AdminRescheduleOptionsResponse'];

export const MIN_REASON = 5;

/**
 * Admin intervention on one booking (DV-A09, D-103): only the transitions the API allows, each with a reason; and a
 * reschedule to a free start for another day or professional. Every action is audited by the API; a refusal (for
 * example a time taken a moment ago) is shown and the page refreshes to the booking's real state.
 */
export function BookingIntervention({
  bookingId,
  version,
  allowed,
  canReschedule,
  today,
  timeZone,
}: {
  bookingId: string;
  version: number;
  allowed: Status[];
  canReschedule: boolean;
  today: string;
  timeZone: string;
}) {
  const t = useTranslations('adminBookings.intervene');
  const tActions = useTranslations('shopBoard.drawer.actions');
  const tv = useTranslations('validation');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const apiMessage = useApiErrorMessage();
  const [target, setTarget] = useState<Status | 'reschedule'>();
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string>();
  const [failure, setFailure] = useState<string>();
  const [done, setDone] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [date, setDate] = useState(today);
  const [professionalId, setProfessionalId] = useState<string>();
  const [options, setOptions] = useState<Options>();
  const [startsAt, setStartsAt] = useState<string>();
  // One idempotency key per chosen move, so a double click or a retry after a network error replays the same change.
  const key = useRef<string>('');

  useEffect(() => {
    if (target !== 'reschedule') return;
    let cancelled = false;
    void (async () => {
      const result = await browserApi.GET('/api/v1/admin/bookings/{bookingId}/reschedule/options', {
        params: { path: { bookingId }, query: { date, professionalId } },
      });
      if (cancelled) return;
      if (result.data) {
        setOptions(result.data);
        setFailure(undefined);
      } else {
        setOptions(undefined);
        setFailure(apiMessage(new ApiError(result.response.status, result.error as never)));
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [target, date, professionalId, bookingId, apiMessage]);

  const open = (next: Status | 'reschedule') => {
    setTarget(next);
    setReason('');
    setReasonError(undefined);
    setFailure(undefined);
    setDone(undefined);
    setStartsAt(undefined);
    key.current = crypto.randomUUID();
  };

  const submit = async () => {
    if (reason.trim().length < MIN_REASON) {
      setReasonError(tv('reasonRequired'));
      return;
    }
    setReasonError(undefined);
    setBusy(true);
    try {
      if (target === 'reschedule') {
        if (!startsAt) return;
        ensureOk(
          await browserApi.POST('/api/v1/admin/bookings/{bookingId}/reschedule', {
            params: { path: { bookingId }, header: { 'Idempotency-Key': key.current } },
            body: { startsAt, professionalId: professionalId ?? null, reason: reason.trim(), version },
          }),
        );
        setDone(t('rescheduled'));
      } else if (target) {
        ensureOk(
          await browserApi.POST('/api/v1/admin/bookings/{bookingId}/transitions', {
            params: { path: { bookingId } },
            body: { to: target, reason: reason.trim(), version },
          }),
        );
        setDone(t('changed'));
      }
      setTarget(undefined);
      router.refresh();
    } catch (error) {
      setFailure(apiMessage(error instanceof ApiError ? error : 'server.unexpected'));
      if (error instanceof ApiError && error.status === 409) router.refresh();
    } finally {
      setBusy(false);
    }
  };

  const title =
    target === 'reschedule' ? t('rescheduleTitle') : target ? t('title', { action: tActions(target) }) : '';

  return (
    <div className="flex flex-col gap-3" data-testid="booking-intervention">
      <div className="flex flex-wrap gap-2">
        {allowed.map((status) => (
          <Button
            key={status}
            size="md"
            variant={status === 'CancelledByShop' ? 'danger' : 'secondary'}
            onClick={() => open(status)}
          >
            {tActions(status)}
          </Button>
        ))}
        {canReschedule && (
          <Button size="md" variant="outline" icon="calendar" onClick={() => open('reschedule')}>
            {t('reschedule')}
          </Button>
        )}
      </div>
      {allowed.length === 0 && !canReschedule && (
        <p className="text-caption text-text-secondary">{t('none')}</p>
      )}
      {done && <InlineAlert tone="success" title={done} />}
      {failure && !target && <InlineAlert tone="danger" title={failure} />}

      <Dialog
        open={target !== undefined}
        onOpenChange={(next) => !next && setTarget(undefined)}
        title={title}
        description={t('auditNote')}
        size={target === 'reschedule' ? 'lg' : 'md'}
        footer={
          <Button
            size="md"
            variant={target === 'CancelledByShop' ? 'dangerSolid' : 'primary'}
            loading={busy}
            disabled={target === 'reschedule' && !startsAt}
            onClick={() => void submit()}
          >
            {target === 'reschedule' ? t('rescheduleSubmit') : t('submit')}
          </Button>
        }
      >
        {target === 'reschedule' && (
          <div className="flex flex-col gap-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="flex flex-col gap-1 text-helper font-bold text-text-strong">
                {t('date')}
                <input
                  type="date"
                  min={today}
                  value={date}
                  onChange={(event) => {
                    setDate(event.target.value || today);
                    setStartsAt(undefined);
                  }}
                  className="min-h-11 rounded-field border-[1.5px] border-border-input bg-surface px-3 font-latin text-input font-normal text-text-primary"
                />
              </label>
              {options && (
                <SelectField
                  label={t('professional')}
                  value={professionalId ?? options.professionalId}
                  onChange={(event) => {
                    setProfessionalId(event.target.value);
                    setStartsAt(undefined);
                  }}
                >
                  {options.professionals.map((p) => (
                    <option key={p.id} value={p.id}>
                      {localizedName(locale, p.nameAr, p.nameEn)}
                    </option>
                  ))}
                </SelectField>
              )}
            </div>
            <fieldset className="flex min-w-0 flex-col gap-2">
              <legend className="mb-1 text-helper font-bold text-text-strong">{t('time')}</legend>
              {options && options.slots.length === 0 && (
                <p className="text-caption text-text-secondary">{t('noSlots')}</p>
              )}
              <div className="flex flex-wrap gap-2" data-testid="reschedule-slots">
                {options?.slots.map((slot) => (
                  <label
                    key={slot.startsAt}
                    className={`relative inline-flex min-h-11 cursor-pointer items-center rounded-button border-[1.5px] px-3 text-label font-bold ${
                      startsAt === slot.startsAt
                        ? 'border-navy-900 bg-navy-900 text-on-navy'
                        : 'border-border-strong bg-surface text-text-strong'
                    }`}
                  >
                    <input
                      type="radio"
                      name="reschedule-slot"
                      className="sr-only"
                      checked={startsAt === slot.startsAt}
                      onChange={() => setStartsAt(slot.startsAt)}
                    />
                    {formatTime(slot.startsAt, locale, timeZone)}
                  </label>
                ))}
              </div>
            </fieldset>
          </div>
        )}
        <TextareaField
          label={t('reason')}
          value={reason}
          maxLength={300}
          onChange={(event) => setReason(event.target.value)}
          error={reasonError}
        />
        {failure && target && <InlineAlert tone="danger" title={failure} />}
      </Dialog>
    </div>
  );
}
