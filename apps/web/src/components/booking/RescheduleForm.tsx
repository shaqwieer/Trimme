'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useEffect, useRef, useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { BookingProgress, DateStrip, HourMinutePicker } from '@/components/ui/booking';
import { Icon, type DesignIconName } from '@/components/ui/icons';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { endOf } from '@/lib/booking/format';
import { keyFor, type KeyedIntent, newIdempotencyKey } from '@/lib/booking/wizard';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatPrice,
  formatTime,
} from '@/lib/i18n/format';
import { formatLocalDate, todayLocal } from '@/lib/i18n/localDate';

/** What the customer is moving, shown in the header and on the review, as the booking wizard shows it. */
export type RescheduleBooking = {
  id: string;
  version: number;
  startsAt: string;
  timeZone: string;
  shopName: string;
  area: string | null;
  itemName: string;
  professionalName: string;
  durationMinutes: number;
  price: number;
  currency: string;
};

type Step = 'date' | 'review';
const PHASES = ['time', 'confirm'] as const;

/**
 * Moving a booking (c-rate 1843–1873, D-144): the same screens as booking it the first time (D-125, D-129) — the
 * nearest free time first, then the days, the day's hours and that hour's minutes, then a review of the old and new
 * times. Same service and professional; only the server's free starts are offered (its own current time does not
 * block it). The current appointment stays booked until the move commits, in one command (D-089); a time taken
 * meanwhile refreshes the times and sends the customer back to pick again.
 */
export function RescheduleForm({ booking }: { booking: RescheduleBooking }) {
  const t = useTranslations('bookings.reschedule');
  const tb = useTranslations('booking');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const queryClient = useQueryClient();
  const apiMessage = useApiErrorMessage();
  const { id: bookingId, timeZone } = booking;
  const today = todayLocal(timeZone);

  const [step, setStep] = useState<Step>('date');
  const [date, setDate] = useState<string>();
  const [start, setStart] = useState<string>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const keyed = useRef<KeyedIntent | undefined>(undefined);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const timesRef = useRef<HTMLDivElement>(null);
  const scrollToTimes = useRef(false);

  const dates = useQuery({
    queryKey: ['reschedule-dates', bookingId],
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/me/bookings/{bookingId}/reschedule/dates', {
          params: { path: { bookingId } },
        }),
      ),
  });

  const slotsOf = (day: string | undefined, enabled: boolean) => ({
    queryKey: ['reschedule-slots', bookingId, day],
    enabled: Boolean(day) && enabled,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/me/bookings/{bookingId}/reschedule/slots', {
          params: { path: { bookingId }, query: { date: day! } },
        }),
      ),
  });

  const slots = useQuery(slotsOf(date, true));
  // The nearest free time, offered first as in the booking wizard: the first day with a free start.
  const nearestDate = dates.data?.dates.find((d) => d.slotCount > 0 && d.date >= today)?.date;
  const nearest = useQuery(slotsOf(nearestDate, step === 'date'));
  const nearestSlot = nearest.data?.bookable === false ? undefined : nearest.data?.slots[0];

  const blocked = dates.data?.bookable === false || slots.data?.bookable === false;
  const dateOk = Boolean(date && dates.data?.dates.some((d) => d.date === date && d.slotCount > 0));
  const chosen = slots.data?.slots.find((s) => s.startsAt === start);

  // Move focus to the step heading when the step changes, so keyboard and screen-reader users follow along.
  const shownStep = useRef(step);
  useEffect(() => {
    if (shownStep.current !== step) {
      shownStep.current = step;
      headingRef.current?.focus();
    }
  }, [step]);

  useEffect(() => {
    if (scrollToTimes.current && dateOk) {
      scrollToTimes.current = false;
      timesRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [date, dateOk]);

  const dayLabel = (day: string) =>
    day === today
      ? tb('today')
      : formatLocalDate(day, locale, { weekday: 'long', day: 'numeric', month: 'long' });
  const when = (instant: string) =>
    `${formatDate(instant, locale, { timeZone })} · ${formatTime(instant, locale, timeZone)}`;

  const pick = (day: string, instant: string | undefined) => {
    setDate(day);
    setStart(instant);
    setError(undefined);
  };

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
          body: { startsAt: start, professionalId: null, version: booking.version },
        }),
      );
      router.replace(`/account/bookings/${bookingId}?rescheduled=1`);
      router.refresh();
    } catch (failure) {
      setPending(false);
      if (failure instanceof ApiError && failure.errorCode === 'booking.slot_unavailable') {
        setError(t('taken'));
        setStart(undefined);
        setStep('date');
        await queryClient.invalidateQueries({ queryKey: ['reschedule-slots', bookingId] });
        await queryClient.invalidateQueries({ queryKey: ['reschedule-dates', bookingId] });
      } else {
        setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
      }
    }
  };

  return (
    <div className="mx-auto flex w-full max-w-[720px] flex-1 flex-col" data-testid="reschedule">
      <header className="flex flex-col gap-4 px-4 pt-4 md:px-6">
        <div className="flex items-center gap-3">
          {step === 'date' ? (
            <Link
              href={`/account/bookings/${bookingId}`}
              aria-label={t('backToBooking')}
              className="inline-flex size-11 items-center justify-center rounded-button border border-border bg-surface text-text-strong hover:bg-bg-subtle"
            >
              <Icon name="chevL" className="size-5" />
            </Link>
          ) : (
            <IconButton
              icon="chevL"
              label={tb('back')}
              variant="outline"
              onClick={() => {
                setError(undefined);
                setStep('date');
              }}
            />
          )}
          <div className="min-w-0 flex-1">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="text-[1.25rem] font-bold text-navy-900 outline-none"
              data-testid="reschedule-title"
            >
              {step === 'date' ? t('title') : t('reviewTitle')}
            </h1>
            <p className="truncate text-helper text-text-secondary">
              {[booking.shopName, booking.area].filter(Boolean).join(' · ')}
            </p>
          </div>
        </div>
        <BookingProgress steps={PHASES.map((p) => tb(`phases.${p}`))} current={step === 'date' ? 0 : 1} />
      </header>

      <section className="flex flex-1 flex-col gap-4 px-4 py-5 md:px-6">
        <InlineAlert tone="warning" title={t('currentTitle')}>
          {t('current', { when: when(booking.startsAt) })}
        </InlineAlert>
        {blocked && <InlineAlert tone="warning" title={t('blocked')} />}

        {step === 'date' && (
          <div className="flex flex-col gap-4">
            <p className="flex items-center gap-2 text-helper text-text-secondary">
              <Icon name="clock" className="size-4" />
              {tb('timeZoneNote')}
            </p>
            {dates.isPending ? (
              <SkeletonList rows={1} label={t('loading')} />
            ) : dates.isError ? (
              <LoadError onRetry={() => dates.refetch()} />
            ) : blocked ? null : (
              <>
                {nearestDate && nearestSlot && (
                  <div
                    className="flex flex-wrap items-center gap-3 rounded-card border-[1.5px] border-brand-500 bg-brand-50 p-4"
                    data-testid="nearest-slot"
                  >
                    <span
                      aria-hidden="true"
                      className="flex size-10 shrink-0 items-center justify-center rounded-full bg-surface text-brand-700"
                    >
                      <Icon name="clock" className="size-5" />
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-helper font-bold text-brand-700">{tb('nearest.title')}</p>
                      <p className="text-[0.9375rem] font-bold text-text-primary">
                        {dayLabel(nearestDate)} · {formatTime(nearestSlot.startsAt, locale, timeZone)}
                      </p>
                    </div>
                    <Button
                      size="md"
                      onClick={() => {
                        pick(nearestDate, nearestSlot.startsAt);
                        setStep('review');
                      }}
                    >
                      {t('nearestPick')}
                    </Button>
                  </div>
                )}
                {nearestDate && nearestSlot && (
                  <p className="text-label font-bold text-text-strong">{tb('nearest.orPick')}</p>
                )}
                <DateStrip
                  name="reschedule-date"
                  today={today}
                  days={dates.data.dates.map((d) => ({ date: d.date, available: d.slotCount > 0 }))}
                  value={dateOk ? date : undefined}
                  onValueChange={(next) => {
                    scrollToTimes.current = true;
                    pick(next, next === date ? start : undefined);
                  }}
                />
                {dateOk && (
                  <p role="status" className="text-helper font-bold text-text-strong">
                    {tb('slotCount', {
                      count: dates.data.dates.find((d) => d.date === date)?.slotCount ?? 0,
                    })}
                  </p>
                )}
                {dateOk && (
                  <div ref={timesRef} className="flex scroll-mt-24 flex-col gap-4" data-testid="day-times">
                    <p className="flex items-center gap-2 rounded-card bg-brand-50 px-4 py-3 text-helper text-brand-700">
                      <Icon name="clock" className="size-4 shrink-0" />
                      {tb('durationNote', {
                        service: booking.itemName,
                        duration: formatDurationMinutes(booking.durationMinutes, locale),
                      })}
                    </p>
                    {slots.isPending ? (
                      <SkeletonList rows={3} label={t('loading')} />
                    ) : slots.isError ? (
                      <LoadError onRetry={() => slots.refetch()} />
                    ) : slots.data.bookable === false ? null : (
                      <HourMinutePicker
                        key={date}
                        name="reschedule-time"
                        timeZone={timeZone}
                        slots={slots.data.slots.map((s) => ({ start: s.startsAt, localTime: s.localTime }))}
                        value={start}
                        onValueChange={(instant) => {
                          setStart(instant);
                          setError(undefined);
                        }}
                      />
                    )}
                  </div>
                )}
                {dates.data.dates.every((d) => d.slotCount === 0) && (
                  <InlineAlert tone="info" title={tb('noDates')} />
                )}
              </>
            )}
          </div>
        )}

        {step === 'review' && start && (
          <div className="flex flex-col gap-4" data-testid="reschedule-review">
            <dl className="flex flex-col divide-y divide-border-row rounded-card border border-border bg-surface px-4">
              <SummaryRow icon="scissors" label={tb('summary.service')} value={booking.itemName} />
              <SummaryRow icon="user" label={tb('summary.professional')} value={booking.professionalName} />
              <SummaryRow
                icon="calendar"
                label={t('summary.from')}
                value={<span className="text-text-secondary line-through">{when(booking.startsAt)}</span>}
              />
              <SummaryRow
                icon="clock"
                label={t('summary.to')}
                value={
                  <span>
                    {date ? dayLabel(date) : formatDate(start, locale, { timeZone })} ·{' '}
                    {formatTime(start, locale, timeZone)} —{' '}
                    {formatTime(endOf(start, booking.durationMinutes), locale, timeZone)}
                  </span>
                }
              />
              <SummaryRow
                icon="info"
                label={tb('summary.duration')}
                value={formatDurationMinutes(booking.durationMinutes, locale)}
              />
            </dl>
            <div className="flex items-center justify-between rounded-card bg-bg-subtle px-4 py-3">
              <span className="text-label font-bold text-text-strong">{tb('summary.total')}</span>
              <span className="font-latin text-[1.125rem] font-extrabold text-navy-900">
                {formatPrice(booking.price, locale, booking.currency)}
              </span>
            </div>
          </div>
        )}

        {error && <InlineAlert tone="danger" title={error} />}
      </section>

      <footer className="sticky bottom-[var(--layout-bottom-nav-height)] z-10 border-t border-border bg-surface/95 backdrop-blur-md lg:bottom-0">
        <div className="flex items-center gap-4 px-4 py-3 md:px-6">
          <div className="min-w-0 flex-1">
            <p className="truncate text-helper text-text-secondary" data-testid="reschedule-footer-note">
              {start ? t('new', { when: when(start) }) : t('pickPrompt')}
            </p>
            <p className="font-latin text-[1.0625rem] font-extrabold text-navy-900">
              {formatPrice(booking.price, locale, booking.currency)}
            </p>
          </div>
          {step === 'review' ? (
            <Button
              size="lg"
              className="min-w-[160px]"
              loading={pending}
              disabled={!start || blocked}
              onClick={submit}
            >
              {t('confirm')}
            </Button>
          ) : (
            <Button
              size="lg"
              className="min-w-[160px]"
              disabled={!start || !chosen || blocked}
              onClick={() => setStep('review')}
            >
              {tb('next')}
            </Button>
          )}
        </div>
      </footer>
    </div>
  );
}

function SummaryRow({ icon, label, value }: { icon: DesignIconName; label: string; value: ReactNode }) {
  return (
    <div className="flex items-center gap-3 py-3">
      <Icon name={icon} className="size-[18px] shrink-0 text-brand-700" />
      <dt className="text-helper text-text-secondary">{label}</dt>
      <dd className="ms-auto text-end text-label font-bold text-text-primary">{value}</dd>
    </div>
  );
}

function LoadError({ onRetry }: { onRetry: () => void }) {
  const tb = useTranslations('booking');
  return (
    <InlineAlert
      tone="danger"
      title={tb('loadError')}
      action={
        <Button variant="outline" size="sm" onClick={onRetry}>
          {tb('retry')}
        </Button>
      }
    />
  );
}
