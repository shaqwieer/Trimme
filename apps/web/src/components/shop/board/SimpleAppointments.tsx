'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { LoyaltyChip } from '@/components/loyalty/LoyaltyChip';
import { StatusBadge } from '@/components/ui/Badge';
import { Button, IconButton } from '@/components/ui/Button';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatNumber, formatTime } from '@/lib/i18n/format';
import { addDays, formatLocalDate, type LocalDate, todayLocal } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { useOperationsEvents } from '../live/OperationsLive';
import { AppointmentDrawer, BOARD_KEY } from './AppointmentDrawer';

type Booking = components['schemas']['ShopBookingResponse'];

/** Still to come: no cancelled, finished or missed appointments on this page (D-134). */
const COMING: Booking['status'][] = ['Pending', 'Confirmed', 'Arrived'];
const PAGE_SIZE = 100;
const MAX_PAGES = 10;
const WEEK = 7;
/** Refetch each minute, so a visit that has just started offers «انتهت الخدمة». */
const REFRESH_MS = 60_000;

async function comingBetween(from: LocalDate, to: LocalDate): Promise<Booking[]> {
  const items: Booking[] = [];
  for (let page = 1; page <= MAX_PAGES; page++) {
    const data = ensureOk(
      await browserApi.GET('/api/v1/shop/bookings', {
        params: { query: { from, to, status: COMING, page, pageSize: PAGE_SIZE } },
      }),
    );
    items.push(...data.items);
    if (data.items.length === 0 || items.length >= data.total) break;
  }
  return items;
}

/**
 * The desk's appointments, kept simple at the client's request (D-134): today's bookings, then the coming days of the
 * week, each with the time, the customer's name, the service and the barber. A week at a time, with next and previous
 * week; nothing cancelled, finished or missed. Tapping one opens its details. A visit under way has «انتهت الخدمة» on
 * its row (D-135): one tap finishes it, so the rest of the booked time is free at once for the desk and online. Counts
 * and other figures stay on the overview.
 */
export function SimpleAppointments({
  today,
  timeZone,
  canUpdate,
}: {
  today: LocalDate;
  timeZone: string;
  canUpdate: boolean;
}) {
  const t = useTranslations('shopBoard.simple');
  const tBoard = useTranslations('shopBoard.appointments');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const [start, setStart] = useState(today);
  const [bookingId, setBookingId] = useState(params.get('booking'));
  const [finishing, setFinishing] = useState<string | null>(null);
  const [finished, setFinished] = useState<string>();
  const [error, setError] = useState<string>();
  const end = addDays(start, WEEK - 1);

  const list = useQuery({
    queryKey: [BOARD_KEY, 'coming', start],
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
    queryFn: () => comingBetween(start, end),
  });

  const finish = async (b: Booking) => {
    setError(undefined);
    setFinished(undefined);
    setFinishing(b.id);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/transitions', {
          params: { path: { bookingId: b.id } },
          body: { to: 'Completed', reason: null, version: b.version },
        }),
      );
      setFinished(t('finished', { name: b.customerName }));
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setFinishing(null);
      await queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
    }
  };

  useOperationsEvents(() => {
    void queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
  });

  const open = (id: string | null) => {
    setBookingId(id);
    const next = new URLSearchParams(window.location.search);
    if (id) next.set('booking', id);
    else next.delete('booking');
    const query = next.toString();
    window.history.replaceState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}`);
  };

  const dayOf = (b: Booking) => todayLocal(timeZone, new Date(b.startsAt));
  const dayTitle = (day: LocalDate) =>
    day === addDays(today, 1)
      ? t('tomorrow')
      : formatLocalDate(day, locale, { weekday: 'long', day: 'numeric', month: 'long' });
  const items = list.data ?? [];
  const todays = start === today ? items.filter((b) => dayOf(b) <= today) : [];
  const later = items.filter((b) => !todays.includes(b));
  const days = [...new Set(later.map(dayOf))];

  const rows = (bookings: Booking[]) => (
    <ul className="flex flex-col divide-y divide-border-row">
      {bookings.map((b) => (
        <li key={b.id} className="flex items-center gap-2" data-testid="coming-appointment">
          <button
            type="button"
            onClick={() => open(b.id)}
            aria-label={tBoard('open', { name: b.customerName })}
            className="flex min-w-0 flex-1 items-center gap-4 px-1 py-3 text-start hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)]"
          >
            <span className="w-20 shrink-0 text-label font-bold text-navy-900">
              {formatTime(b.startsAt, locale, timeZone)}
            </span>
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="flex flex-wrap items-center gap-2 text-body font-bold text-text-primary">
                {b.customerName}
                {b.loyaltyReward && <LoyaltyChip reward={b.loyaltyReward} />}
              </span>
              <span className="text-helper text-text-secondary">
                {localizedName(locale, b.item.nameAr, b.item.nameEn)} ·{' '}
                {localizedName(locale, b.professional.nameAr, b.professional.nameEn)}
              </span>
            </span>
            {b.status !== 'Confirmed' && <StatusBadge kind="booking" status={b.status} size="sm" />}
          </button>
          {canUpdate && b.allowedTransitions.includes('Completed') && (
            <Button
              size="sm"
              icon="check"
              className="shrink-0"
              loading={finishing === b.id}
              disabled={finishing !== null}
              aria-label={t('finishLabel', { name: b.customerName })}
              onClick={() => void finish(b)}
            >
              {t('finish')}
            </Button>
          )}
        </li>
      ))}
    </ul>
  );

  return (
    <div className="flex max-w-[760px] flex-col gap-4" data-testid="simple-appointments">
      <div className="flex items-center justify-between gap-2">
        <IconButton
          icon="chevL"
          label={t('previousWeek')}
          variant="outline"
          disabled={start <= today}
          onClick={() => setStart(addDays(start, -WEEK) < today ? today : addDays(start, -WEEK))}
        />
        <div className="flex min-w-0 flex-col items-center text-center" data-testid="coming-week">
          {start === today && <span className="text-label font-bold text-text-primary">{t('thisWeek')}</span>}
          <span
            className={
              start === today ? 'text-helper text-text-secondary' : 'text-label font-bold text-text-primary'
            }
          >
            {t('week', {
              from: formatLocalDate(start, locale, { day: 'numeric', month: 'long' }),
              to: formatLocalDate(end, locale, { day: 'numeric', month: 'long' }),
            })}
          </span>
        </div>
        <IconButton
          icon="chevR"
          label={t('nextWeek')}
          variant="outline"
          onClick={() => setStart(addDays(start, WEEK))}
        />
      </div>

      {error && <InlineAlert tone="danger" title={error} />}
      <p
        role="status"
        className={
          finished
            ? 'rounded-card bg-status-completed-bg p-3 text-label font-bold text-status-completed-fg'
            : 'sr-only'
        }
      >
        {finished ?? ''}
      </p>

      {list.isPending ? (
        <SkeletonList rows={5} label={tBoard('loading')} />
      ) : list.isError ? (
        <InlineAlert tone="danger" title={tBoard('loadError')} />
      ) : (
        <>
          {start === today && (
            <section
              className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4 shadow-e1"
              aria-labelledby="coming-today"
              data-testid="coming-today"
            >
              <h2 id="coming-today" className="text-h3 font-bold text-navy-900">
                {t('today')} <span className="font-latin">({formatNumber(todays.length, locale)})</span>
              </h2>
              {todays.length === 0 ? (
                <p className="text-caption text-text-secondary">{t('noneToday')}</p>
              ) : (
                rows(todays)
              )}
            </section>
          )}
          <section
            className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1"
            aria-labelledby="coming-later"
            data-testid="coming-later"
          >
            <h2 id="coming-later" className="text-h3 font-bold text-navy-900">
              {t('upcoming')}
            </h2>
            {days.length === 0 ? (
              start === today ? (
                <p className="text-caption text-text-secondary">{t('noneUpcoming')}</p>
              ) : (
                <EmptyState icon="calendar" title={t('noneUpcoming')} />
              )
            ) : (
              days.map((day) => (
                <div key={day} className="flex flex-col">
                  <h3 className="border-b border-border pb-1 text-label font-bold text-text-strong">
                    {dayTitle(day)}
                  </h3>
                  {rows(later.filter((b) => dayOf(b) === day))}
                </div>
              ))
            )}
          </section>
        </>
      )}

      <AppointmentDrawer
        bookingId={bookingId}
        timeZone={timeZone}
        canUpdate={canUpdate}
        onClose={() => open(null)}
      />
    </div>
  );
}
