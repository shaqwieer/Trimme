'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useState } from 'react';
import { LoyaltyChip } from '@/components/loyalty/LoyaltyChip';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button, IconButton } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/overlays';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { type AppLocale, formatNumber, formatTime } from '@/lib/i18n/format';
import { addDays, formatLocalDate, type LocalDate, todayLocal } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { cn } from '@/lib/cn';
import { useOperationsEvents } from '../live/OperationsLive';
import { AppointmentDrawer, BOARD_KEY } from './AppointmentDrawer';
import { CustomerVisitBadge } from './CustomerVisitBadge';

type Booking = components['schemas']['ShopBookingResponse'];
type Status = components['schemas']['BookingStatus'];

/** Still to come: no cancelled, finished or missed appointments on this page (D-134). */
const COMING: Booking['status'][] = ['Pending', 'Confirmed', 'Arrived'];
const PAGE_SIZE = 100;
const MAX_PAGES = 10;
const WEEK = 7;
/** Refetch each minute, so «وصل», «ما حضر» and red bookings appear on time. */
const REFRESH_MS = 60_000;

/** The row's quick actions, in the order a visit goes (D-147); rejecting and cancelling ask for a reason in the details. */
const QUICK: Status[] = ['Confirmed', 'Arrived', 'Completed', 'NoShow'];
/** A red booking's row closes it (D-147). */
const OVERDUE_QUICK: Status[] = ['Completed', 'NoShow'];
/** These ask first: they cannot be undone. */
const CONFIRM_FIRST: Status[] = ['Completed', 'NoShow'];

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
 * The shop's appointments, kept simple at the client's request (D-134, D-136). A barber (D-145, D-147) sees first what
 * any barber of the shop may act on: bookings waiting to be accepted, red ones (open a quarter hour past their end, for
 * three days, with the barber they belong to) and those closed late (with who closed them); then their own day and the
 * coming days of the week. On a row: «قبول», «وصل», then «انتهت الخدمة» (once; the booking then leaves the list) and,
 * from a quarter hour late, «ما حضر». The owner sees the same lists with nothing to press (D-147: «صاحب المحل للمتابعة
 * بس»), and whether each customer is new or returning (D-135). Tapping a row opens its details.
 */
export function SimpleAppointments({
  today,
  timeZone,
  canUpdate,
  canReschedule = false,
  canMove = false,
  ownProfessionalId = null,
}: {
  today: LocalDate;
  timeZone: string;
  canUpdate: boolean;
  canReschedule?: boolean;
  canMove?: boolean;
  /** A barber's own professional (D-145): their colleagues' bookings allow less. */
  ownProfessionalId?: string | null;
}) {
  const t = useTranslations('shopBoard.simple');
  const tBoard = useTranslations('shopBoard.appointments');
  const tActions = useTranslations('shopBoard.drawer.actions');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const [start, setStart] = useState(today);
  const [bookingId, setBookingId] = useState(params.get('booking'));
  const [acting, setActing] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<{ booking: Booking; to: Status } | null>(null);
  const [done, setDone] = useState<string>();
  const [error, setError] = useState<string>();
  const end = addDays(start, WEEK - 1);

  const list = useQuery({
    queryKey: [BOARD_KEY, 'coming', start],
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
    queryFn: () => comingBetween(start, end),
  });
  const attention = useQuery({
    queryKey: [BOARD_KEY, 'attention'],
    refetchInterval: REFRESH_MS,
    queryFn: async () => ensureOk(await browserApi.GET('/api/v1/shop/bookings/attention')),
  });

  const act = async (b: Booking, to: Status) => {
    setError(undefined);
    setDone(undefined);
    setActing(b.id);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/transitions', {
          params: { path: { bookingId: b.id } },
          body: { to, reason: null, version: b.version },
        }),
      );
      setDone(t(`done.${to as 'Confirmed' | 'Arrived' | 'Completed' | 'NoShow'}`, { name: b.customerName }));
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setActing(null);
      setConfirming(null);
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
  const pending = attention.data?.pending ?? [];
  const overdue = attention.data?.overdue ?? [];
  const closedLate = attention.data?.closedLate ?? [];
  // Shown once: a booking in the shop-wide sections leaves the day lists.
  const shown = new Set([...pending, ...overdue].map((b) => b.id));
  const items = (list.data ?? []).filter((b) => !shown.has(b.id));
  const todays = start === today ? items.filter((b) => dayOf(b) <= today) : [];
  const later = items.filter((b) => !todays.includes(b));
  const days = [...new Set(later.map(dayOf))];

  // A red booking is closed, not started: «انتهت الخدمة» or «ما حضر» (a pending one is rejected from its details).
  const quick = (b: Booking) =>
    canUpdate
      ? (b.overdue ? OVERDUE_QUICK : QUICK).filter((to) => b.allowedTransitions.includes(to)).slice(0, 2)
      : [];

  // On the red card the secondary grey is below 4.5:1 (#5f6f80 on danger-50), so its rows' second line is darker.
  const rows = (bookings: Booking[], extra?: (b: Booking) => ReactNode, onDanger = false) => (
    <ul className="flex flex-col divide-y divide-border-row">
      {bookings.map((b) => (
        <li
          key={b.id}
          className="flex flex-wrap items-center gap-2 sm:flex-nowrap"
          data-testid="coming-appointment"
        >
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
                <CustomerVisitBadge visit={b.visit} />
              </span>
              <span className={cn('text-helper', onDanger ? 'text-text-strong' : 'text-text-secondary')}>
                {localizedName(locale, b.item.nameAr, b.item.nameEn)} —{' '}
                {localizedName(locale, b.professional.nameAr, b.professional.nameEn)}
              </span>
              {extra?.(b)}
            </span>
            {b.overdue ? (
              <Badge tone="danger" size="sm">
                {t('overdueBadge')}
              </Badge>
            ) : (
              b.status !== 'Confirmed' && <StatusBadge kind="booking" status={b.status} size="sm" />
            )}
          </button>
          {quick(b).length > 0 && (
            <span className="flex shrink-0 gap-1.5">
              {quick(b).map((to) => (
                <Button
                  key={to}
                  size="sm"
                  variant={to === 'NoShow' ? 'outline' : 'primary'}
                  icon={to === 'Completed' ? 'check' : undefined}
                  loading={acting === b.id && confirming?.to !== to}
                  disabled={acting !== null}
                  aria-label={t('actionLabel', {
                    action: t(`row.${to as 'Confirmed'}`),
                    name: b.customerName,
                  })}
                  onClick={() =>
                    CONFIRM_FIRST.includes(to) ? setConfirming({ booking: b, to }) : void act(b, to)
                  }
                >
                  {t(`row.${to as 'Confirmed'}`)}
                </Button>
              ))}
            </span>
          )}
        </li>
      ))}
    </ul>
  );

  const section = (id: string, title: string, children: ReactNode, tone?: 'danger' | 'muted') => (
    <section
      className={cn(
        'flex flex-col gap-2 rounded-card border p-4 shadow-e1',
        tone === 'danger' ? 'border-danger-border bg-danger-50' : 'border-border bg-surface',
      )}
      aria-labelledby={id}
      data-testid={id}
    >
      <h2
        id={id}
        className={cn('text-h3 font-bold', tone === 'danger' ? 'text-danger-700' : 'text-navy-900')}
      >
        {title}
      </h2>
      {children}
    </section>
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
          done
            ? 'rounded-card bg-status-completed-bg p-3 text-label font-bold text-status-completed-fg'
            : 'sr-only'
        }
      >
        {done ?? ''}
      </p>

      {overdue.length > 0 &&
        section(
          'attention-overdue',
          `${t('overdue')} (${formatNumber(overdue.length, locale)})`,
          <>
            <p className="text-helper text-danger-700">{t('overdueHint')}</p>
            {rows(
              overdue,
              (b) => (
                <span className="text-helper font-bold text-danger-700">
                  {t('responsible', {
                    name: localizedName(locale, b.professional.nameAr, b.professional.nameEn),
                  })}
                </span>
              ),
              true,
            )}
          </>,
          'danger',
        )}
      {pending.length > 0 &&
        section(
          'attention-pending',
          `${t('pending')} (${formatNumber(pending.length, locale)})`,
          <>
            <p className="text-helper text-text-secondary">{t('pendingHint')}</p>
            {rows(pending)}
          </>,
        )}

      {list.isPending ? (
        <SkeletonList rows={5} label={tBoard('loading')} />
      ) : list.isError ? (
        <InlineAlert tone="danger" title={tBoard('loadError')} />
      ) : (
        <>
          {start === today &&
            section(
              'coming-today',
              `${t('today')} (${formatNumber(todays.length, locale)})`,
              todays.length === 0 ? (
                <p className="text-caption text-text-secondary">{t('noneToday')}</p>
              ) : (
                rows(todays)
              ),
            )}
          {section(
            'coming-later',
            t('upcoming'),
            days.length === 0 ? (
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
            ),
          )}
        </>
      )}

      {closedLate.length > 0 &&
        section(
          'attention-closed-late',
          t('closedLate'),
          <ul className="flex flex-col divide-y divide-border-row">
            {closedLate.map((b) => (
              <li key={b.id}>
                <button
                  type="button"
                  onClick={() => open(b.id)}
                  className="flex w-full flex-col items-start gap-0.5 px-1 py-2.5 text-start hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)]"
                >
                  <span className="text-label font-bold text-text-primary">
                    {formatLocalDate(dayOf(b), locale, { day: 'numeric', month: 'long' })} —{' '}
                    {formatTime(b.startsAt, locale, timeZone)} — {b.customerName}
                  </span>
                  <span className="text-helper text-text-secondary">
                    {t('closedLateBy', {
                      responsible: localizedName(locale, b.professional.nameAr, b.professional.nameEn),
                      closer: b.closedBy ?? '—',
                      status: tActions(b.status),
                    })}
                  </span>
                </button>
              </li>
            ))}
          </ul>,
          'muted',
        )}

      <ConfirmDialog
        open={confirming !== null}
        onOpenChange={(open) => !open && acting === null && setConfirming(null)}
        tone={confirming?.to === 'NoShow' ? 'danger' : 'default'}
        title={
          confirming?.to === 'NoShow'
            ? t('noShowTitle', { name: confirming.booking.customerName })
            : t('finishTitle', { name: confirming?.booking.customerName ?? '' })
        }
        body={confirming?.to === 'NoShow' ? t('noShowBody') : t('finishBody')}
        confirmLabel={confirming?.to === 'NoShow' ? t('noShowConfirm') : t('finishConfirm')}
        cancelLabel={t('finishKeep')}
        loading={acting !== null}
        onConfirm={() => confirming && void act(confirming.booking, confirming.to)}
      />

      <AppointmentDrawer
        bookingId={bookingId}
        timeZone={timeZone}
        canUpdate={canUpdate}
        canReschedule={canReschedule}
        canMove={canMove}
        ownProfessionalId={ownProfessionalId}
        onClose={() => open(null)}
      />
    </div>
  );
}
