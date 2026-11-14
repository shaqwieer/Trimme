'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useRef, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { DateStrip, HourMinutePicker } from '@/components/ui/booking';
import { Dialog } from '@/components/ui/overlays';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { keyFor, type KeyedIntent, newIdempotencyKey } from '@/lib/booking/wizard';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { formatLocalDate, todayLocal } from '@/lib/i18n/localDate';

/** The booking a dialog changes: what it shows, and the version the change is checked against. */
export type ChangeTarget = {
  id: string;
  version: number;
  startsAt: string;
  customerName: string;
};

type DialogProps = {
  booking: ChangeTarget;
  timeZone: string;
  onClose: () => void;
  /** After a change is saved (refetch the board and the drawer). */
  onChanged: () => void | Promise<void>;
};

const KEY = 'shop-booking-change';

/** A failure as the dialog explains it: a time or barber taken meanwhile, a stale version, or the API's message. */
function useFailureMessage() {
  const t = useTranslations('shopBoard.drawer');
  const apiMessage = useApiErrorMessage();
  return (failure: unknown, conflict: string) => {
    if (failure instanceof ApiError && failure.status === 409) {
      return failure.errorCode === 'booking.slot_unavailable' ||
        failure.errorCode === 'booking.professional_busy'
        ? conflict
        : t('stale');
    }
    return apiMessage(failure instanceof ApiError ? failure : 'server.unexpected');
  };
}

/**
 * A new time for a customer's booking, with the same barber and service (D-145): the free starts the server offers
 * (the booking's own time does not block it), by day and then hour and minute, as the booking screens show them. The
 * customer is told. A time taken meanwhile refreshes the times.
 */
export function RescheduleDialog({ booking, timeZone, onClose, onChanged }: DialogProps) {
  const t = useTranslations('shopBoard.drawer.change');
  const tb = useTranslations('booking');
  const locale = useLocale() as AppLocale;
  const queryClient = useQueryClient();
  const failureMessage = useFailureMessage();
  const today = todayLocal(timeZone);
  const [date, setDate] = useState<string>();
  const [start, setStart] = useState<string>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  // One key per chosen time, reused when the same save is retried (D-089).
  const keyed = useRef<KeyedIntent | undefined>(undefined);

  const dates = useQuery({
    queryKey: [KEY, 'dates', booking.id],
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/bookings/{bookingId}/reschedule/dates', {
          params: { path: { bookingId: booking.id } },
        }),
      ),
  });
  const slots = useQuery({
    queryKey: [KEY, 'slots', booking.id, date],
    enabled: Boolean(date),
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/bookings/{bookingId}/reschedule/slots', {
          params: { path: { bookingId: booking.id }, query: { date: date! } },
        }),
      ),
  });

  const blocked = dates.data?.bookable === false || slots.data?.bookable === false;
  const dateOk = Boolean(date && dates.data?.dates.some((d) => d.date === date && d.slotCount > 0));
  const chosen = slots.data?.slots.find((s) => s.startsAt === start);
  const when = (instant: string) =>
    `${formatDate(instant, locale, { timeZone })} · ${formatTime(instant, locale, timeZone)}`;

  const save = async () => {
    if (!start) return;
    setError(undefined);
    keyed.current = keyFor(
      keyed.current,
      { offerId: booking.id, pro: '', startsAt: start, note: 'shop-reschedule' },
      newIdempotencyKey,
    );
    setPending(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/reschedule', {
          params: { path: { bookingId: booking.id }, header: { 'Idempotency-Key': keyed.current.key } },
          body: { startsAt: start, version: booking.version },
        }),
      );
      await onChanged();
      onClose();
    } catch (failure) {
      setError(failureMessage(failure, t('taken')));
      if (failure instanceof ApiError && failure.errorCode === 'booking.slot_unavailable') {
        setStart(undefined);
        await queryClient.invalidateQueries({ queryKey: [KEY] });
      }
    } finally {
      setPending(false);
    }
  };

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      size="lg"
      title={t('rescheduleTitle', { name: booking.customerName })}
      description={t('rescheduleBody')}
      footer={
        <>
          <Button
            className="flex-1"
            loading={pending}
            disabled={!chosen || blocked}
            onClick={() => void save()}
          >
            {t('confirmReschedule')}
          </Button>
          <Button variant="outline" className="flex-1" onClick={onClose}>
            {t('keep')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4" data-testid="shop-reschedule">
        <p className="text-helper text-text-secondary">{t('current', { when: when(booking.startsAt) })}</p>
        {dates.isPending ? (
          <SkeletonList rows={1} label={t('loading')} />
        ) : dates.isError ? (
          <InlineAlert tone="danger" title={tb('loadError')} />
        ) : blocked ? (
          <InlineAlert tone="warning" title={t('blocked')} />
        ) : dates.data.dates.every((d) => d.slotCount === 0) ? (
          <InlineAlert tone="info" title={t('noDates')} />
        ) : (
          <>
            <p className="text-label font-bold text-text-strong">{t('pickDay')}</p>
            <DateStrip
              name="shop-reschedule-date"
              today={today}
              days={dates.data.dates.map((d) => ({ date: d.date, available: d.slotCount > 0 }))}
              value={dateOk ? date : undefined}
              onValueChange={(next) => {
                setDate(next);
                setStart(undefined);
                setError(undefined);
              }}
            />
            {dateOk && (
              <>
                <p className="text-label font-bold text-text-strong">
                  {t('pickTime')} ·{' '}
                  {formatLocalDate(date!, locale, { weekday: 'long', day: 'numeric', month: 'long' })}
                </p>
                {slots.isPending ? (
                  <SkeletonList rows={2} label={t('loading')} />
                ) : slots.isError ? (
                  <InlineAlert tone="danger" title={tb('loadError')} />
                ) : (
                  <HourMinutePicker
                    key={date}
                    name="shop-reschedule-time"
                    timeZone={timeZone}
                    slots={slots.data.slots.map((s) => ({ start: s.startsAt, localTime: s.localTime }))}
                    value={start}
                    onValueChange={(instant) => {
                      setStart(instant);
                      setError(undefined);
                    }}
                  />
                )}
              </>
            )}
          </>
        )}
        {error && <InlineAlert tone="danger" title={error} />}
      </div>
    </Dialog>
  );
}

/**
 * «نقل الحجز إلى حلاق آخر» (D-145): the same time with a colleague of the same shop who does the booked service. Busy
 * colleagues are listed but cannot be chosen. The customer is told, and may cancel or change the booking until it
 * starts. Only the booking moves: a barber never leaves their shop (D-011).
 */
export function MoveDialog({ booking, timeZone, onClose, onChanged }: DialogProps) {
  const t = useTranslations('shopBoard.drawer.change');
  const tb = useTranslations('booking');
  const locale = useLocale() as AppLocale;
  const queryClient = useQueryClient();
  const failureMessage = useFailureMessage();
  const [chosen, setChosen] = useState<string>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const keyed = useRef<KeyedIntent | undefined>(undefined);

  const options = useQuery({
    queryKey: [KEY, 'professionals', booking.id],
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/bookings/{bookingId}/professional-options', {
          params: { path: { bookingId: booking.id } },
        }),
      ),
  });
  const choice = options.data?.professionals.find((p) => p.id === chosen && p.free);

  const save = async () => {
    if (!choice) return;
    setError(undefined);
    keyed.current = keyFor(
      keyed.current,
      { offerId: booking.id, pro: choice.id, startsAt: booking.startsAt, note: 'shop-move' },
      newIdempotencyKey,
    );
    setPending(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/professional', {
          params: { path: { bookingId: booking.id }, header: { 'Idempotency-Key': keyed.current.key } },
          body: { professionalId: choice.id, version: booking.version },
        }),
      );
      await onChanged();
      onClose();
    } catch (failure) {
      setError(failureMessage(failure, t('busyNow')));
      if (failure instanceof ApiError && failure.errorCode === 'booking.professional_busy') {
        setChosen(undefined);
        await queryClient.invalidateQueries({ queryKey: [KEY, 'professionals', booking.id] });
      }
    } finally {
      setPending(false);
    }
  };

  const when = `${formatDate(booking.startsAt, locale, { timeZone })} · ${formatTime(booking.startsAt, locale, timeZone)}`;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      size="md"
      title={t('moveTitle', { name: booking.customerName })}
      description={t('moveBody', { when })}
      footer={
        <>
          <Button className="flex-1" loading={pending} disabled={!choice} onClick={() => void save()}>
            {t('confirmMove')}
          </Button>
          <Button variant="outline" className="flex-1" onClick={onClose}>
            {t('keep')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3" data-testid="shop-move">
        {options.isPending ? (
          <SkeletonList rows={2} label={t('loading')} />
        ) : options.isError ? (
          <InlineAlert tone="danger" title={tb('loadError')} />
        ) : options.data.professionals.length === 0 ? (
          <InlineAlert tone="info" title={t('noColleagues')} />
        ) : (
          <fieldset className="flex flex-col gap-2">
            <legend className="sr-only">{t('move')}</legend>
            {options.data.professionals.map((p) => (
              <RadioCard
                key={p.id}
                name="shop-move-professional"
                value={p.id}
                checked={chosen === p.id}
                disabled={!p.free}
                onChange={() => {
                  setChosen(p.id);
                  setError(undefined);
                }}
                aside={
                  <span
                    className={p.free ? 'text-helper text-success-700' : 'text-helper text-text-tertiary'}
                  >
                    {p.free ? t('free') : t('busy')}
                  </span>
                }
              >
                <span className="text-label font-bold text-text-primary">
                  {localizedName(locale, p.nameAr, p.nameEn)}
                </span>
              </RadioCard>
            ))}
          </fieldset>
        )}
        {error && <InlineAlert tone="danger" title={error} />}
      </div>
    </Dialog>
  );
}
