'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useRef, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { DateStrip, SlotGrid } from '@/components/ui/booking';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { keyFor, type KeyedIntent, newIdempotencyKey } from '@/lib/booking/wizard';
import { type AppLocale, formatDate, formatTime } from '@/lib/i18n/format';
import { formatLocalDate } from '@/lib/i18n/localDate';

/**
 * Moving a booking (c-rate 1843–1873): same service and professional, a new date and time from the server's slots
 * (its own current time does not block it). The current appointment stays booked until the move commits, in one
 * command (D-089); a time taken meanwhile refreshes the slots.
 */
export function RescheduleForm({
  bookingId,
  version,
  startsAt,
  timeZone,
}: {
  bookingId: string;
  version: number;
  startsAt: string;
  timeZone: string;
}) {
  const t = useTranslations('bookings.reschedule');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const queryClient = useQueryClient();
  const apiMessage = useApiErrorMessage();
  const [date, setDate] = useState<string>();
  const [start, setStart] = useState<string>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const keyed = useRef<KeyedIntent | undefined>(undefined);

  const dates = useQuery({
    queryKey: ['reschedule-dates', bookingId],
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/me/bookings/{bookingId}/reschedule/dates', {
          params: { path: { bookingId } },
        }),
      ),
  });

  const slots = useQuery({
    queryKey: ['reschedule-slots', bookingId, date],
    enabled: Boolean(date),
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/me/bookings/{bookingId}/reschedule/slots', {
          params: { path: { bookingId }, query: { date: date! } },
        }),
      ),
  });

  const submit = async () => {
    if (!start) return;
    setError(undefined);
    keyed.current = keyFor(
      keyed.current,
      { offerId: bookingId, pro: '', startsAt: start, note: '' },
      newIdempotencyKey,
    );
    setPending(true);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/me/bookings/{bookingId}/reschedule', {
          params: { path: { bookingId }, header: { 'Idempotency-Key': keyed.current.key } },
          body: { startsAt: start, professionalId: null, version },
        }),
      );
      router.replace(`/account/bookings/${bookingId}?rescheduled=1`);
      router.refresh();
    } catch (failure) {
      setPending(false);
      if (failure instanceof ApiError && failure.errorCode === 'booking.slot_unavailable') {
        setError(t('taken'));
        setStart(undefined);
        await queryClient.invalidateQueries({ queryKey: ['reschedule-slots', bookingId] });
        await queryClient.invalidateQueries({ queryKey: ['reschedule-dates', bookingId] });
      } else {
        setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
      }
    }
  };

  const blocked = dates.data?.bookable === false;

  return (
    <div className="flex flex-col gap-5">
      <InlineAlert tone="warning" title={t('currentTitle')}>
        {t('current', {
          when: `${formatDate(startsAt, locale, { timeZone })} ${formatTime(startsAt, locale, timeZone)}`,
        })}
      </InlineAlert>
      {blocked && <InlineAlert tone="warning" title={t('blocked')} />}

      <section aria-labelledby="reschedule-date" className="flex flex-col gap-3">
        <h2 id="reschedule-date" className="text-label font-bold text-text-primary">
          {t('pickDate')}
        </h2>
        {dates.isPending ? (
          <SkeletonList rows={1} label={t('loading')} />
        ) : dates.isError ? (
          <InlineAlert
            tone="danger"
            title={apiMessage(dates.error instanceof ApiError ? dates.error : 'server.unexpected') ?? ''}
          />
        ) : (
          <DateStrip
            name="reschedule-date"
            days={dates.data.dates.map((d) => ({ date: d.date, available: d.slotCount > 0 }))}
            value={date}
            onValueChange={(next) => {
              setDate(next);
              setStart(undefined);
              setError(undefined);
            }}
          />
        )}
      </section>

      {date && (
        <section aria-labelledby="reschedule-time" className="flex flex-col gap-3">
          <h2 id="reschedule-time" className="text-label font-bold text-text-primary">
            {t('pickTime', {
              date: formatLocalDate(date, locale, { weekday: 'long', day: 'numeric', month: 'long' }),
            })}
          </h2>
          {slots.isPending ? (
            <SkeletonList rows={2} label={t('loading')} />
          ) : slots.isError ? (
            <InlineAlert
              tone="danger"
              title={apiMessage(slots.error instanceof ApiError ? slots.error : 'server.unexpected') ?? ''}
            />
          ) : (
            <SlotGrid
              name="reschedule-time"
              timeZone={timeZone}
              slots={slots.data.slots.map((s) => ({ start: s.startsAt }))}
              value={start}
              onValueChange={setStart}
            />
          )}
        </section>
      )}

      {error && <InlineAlert tone="danger" title={error} />}

      <div className="sticky bottom-[var(--layout-bottom-nav-height)] z-10 flex items-center gap-4 border-t border-border bg-surface/95 py-3 backdrop-blur-md lg:bottom-0">
        <p className="min-w-0 flex-1 truncate text-helper text-text-secondary">
          {start
            ? t('new', {
                when: `${formatDate(start, locale, { timeZone })} ${formatTime(start, locale, timeZone)}`,
              })
            : t('pickPrompt')}
        </p>
        <Button disabled={!start || blocked} loading={pending} onClick={submit}>
          {t('confirm')}
        </Button>
      </div>
    </div>
  );
}
