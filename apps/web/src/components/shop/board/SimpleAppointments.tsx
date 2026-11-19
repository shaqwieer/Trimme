'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useState } from 'react';
import { LoyaltyChip } from '@/components/loyalty/LoyaltyChip';
import { useDisplayCalendar } from '@/components/providers/CalendarProvider';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button, ButtonLink, IconButton } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { ConfirmDialog } from '@/components/ui/overlays';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { bookingSource } from '@/lib/booking/format';
import { cn } from '@/lib/cn';
import { type AppLocale, formatNumber, formatTime } from '@/lib/i18n/format';
import { addDays, formatLocalDate, type LocalDate, todayLocal } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { useOperationsEvents } from '../live/OperationsLive';
import { AppointmentDrawer, BOARD_KEY } from './AppointmentDrawer';
import { CustomerVisitBadge } from './CustomerVisitBadge';

type Booking = components['schemas']['ShopBookingResponse'];
type Status = components['schemas']['BookingStatus'];

/** One of the shop's barbers, for the «جدولي / الكل / a colleague» chips. */
export type DayBarber = { id: string; nameAr: string; nameEn: string | null };

/** Still to come: no cancelled, finished or missed appointments on the day (D-134). */
const COMING: Booking['status'][] = ['Pending', 'Confirmed', 'Arrived'];
const PAGE_SIZE = 100;
const MAX_PAGES = 10;
/** Refetch each minute, so «وصل», «ما حضر» and red bookings appear on time. */
const REFRESH_MS = 60_000;

/** The row's quick actions, in the order a visit goes (D-147); rejecting and cancelling ask for a reason in the details. */
const QUICK: Status[] = ['Confirmed', 'Arrived', 'Completed', 'NoShow'];
/** A red booking's row: the usual buttons, «وصل» and «ما حضر», for any barber (D-151), and «انتهت الخدمة». */
const OVERDUE_QUICK: Status[] = ['Arrived', 'Completed', 'NoShow'];
/** These ask first: they cannot be undone. */
const CONFIRM_FIRST: Status[] = ['Completed', 'NoShow'];

/** Whose day is shown: the barber's own («جدولي»), everyone's («الكل») or one barber's. */
type Scope = { kind: 'mine' } | { kind: 'all' } | { kind: 'barber'; id: string };

async function comingOn(day: LocalDate, scope: Scope): Promise<Booking[]> {
  const items: Booking[] = [];
  const who =
    scope.kind === 'all' ? { all: true } : scope.kind === 'barber' ? { professionalId: scope.id } : {};
  for (let page = 1; page <= MAX_PAGES; page++) {
    const data = ensureOk(
      await browserApi.GET('/api/v1/shop/bookings', {
        params: { query: { from: day, to: day, status: COMING, page, pageSize: PAGE_SIZE, ...who } },
      }),
    );
    items.push(...data.items);
    if (data.items.length === 0 || items.length >= data.total) break;
  }
  return items;
}

/**
 * The shop's day (D-151, the client's 1-1, «شاشة اليوم»), as in the Groom Here app but in the site's look:
 * - for a barber, quick buttons first: «زبون حضوري», «حجز بالتلفون», «بريك», «إيقاف الحجز»;
 * - then what any barber of the shop must close: «حجوزات تحتاج إقفال», red as soon as their booked end passed, each with
 *   its responsible barber; and the bookings waiting to be accepted;
 * - then one day at a time («اليوم · الخميس ١٩ نوفمبر») with «جدولي», «الكل» or any colleague: a colleague's bookings are
 *   read-only, and their customers' numbers stay hidden (the API decides);
 * - last, those closed late, with «المسؤول: … — قفله: …».
 * On a row: «قبول», «وصل», then «انتهت الخدمة» (once; the booking then leaves the list) and, from a quarter hour late,
 * «ما حضر»; the buttons sit under the text so a long name never squeezes it. The owner sees the same day, everyone's by
 * default, with nothing to press (D-147) and whether each customer is new or returning (D-135). A row opens its details.
 */
export function SimpleAppointments({
  today,
  timeZone,
  canUpdate,
  canReschedule = false,
  canMove = false,
  ownProfessionalId = null,
  barbers = [],
  quickActions = false,
}: {
  today: LocalDate;
  timeZone: string;
  canUpdate: boolean;
  canReschedule?: boolean;
  canMove?: boolean;
  /** A barber's own professional (D-145): their colleagues' bookings allow less. */
  ownProfessionalId?: string | null;
  /** The shop's active barbers, for the chips. */
  barbers?: DayBarber[];
  /** A barber's quick buttons (walk-in, phone booking, break, pause); the owner has none (D-146, D-147). */
  quickActions?: boolean;
}) {
  const t = useTranslations('shopBoard.simple');
  const tBoard = useTranslations('shopBoard.appointments');
  const tActions = useTranslations('shopBoard.drawer.actions');
  const tChannel = useTranslations('shopBoard.drawer.channel');
  const locale = useLocale() as AppLocale;
  const calendar = useDisplayCalendar();
  const apiMessage = useApiErrorMessage();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const [day, setDay] = useState(today);
  const [scope, setScope] = useState<Scope>(ownProfessionalId ? { kind: 'mine' } : { kind: 'all' });
  const [bookingId, setBookingId] = useState(params.get('booking'));
  const [acting, setActing] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<{ booking: Booking; to: Status } | null>(null);
  const [done, setDone] = useState<string>();
  const [error, setError] = useState<string>();

  const list = useQuery({
    queryKey: [BOARD_KEY, 'day', day, scope],
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
    queryFn: () => comingOn(day, scope),
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
  const barberName = (b: Booking) => localizedName(locale, b.professional.nameAr, b.professional.nameEn);
  const pending = attention.data?.pending ?? [];
  const overdue = attention.data?.overdue ?? [];
  const closedLate = attention.data?.closedLate ?? [];
  // Shown once: a booking in the shop-wide sections leaves the day.
  const shown = new Set([...pending, ...overdue].map((b) => b.id));
  const items = (list.data ?? []).filter((b) => !shown.has(b.id) && (day !== today || dayOf(b) <= today));
  const showsBarber = scope.kind === 'all';
  const colleagues = barbers.filter((b) => b.id !== ownProfessionalId);

  // A red booking is closed, not started late: «وصل», «انتهت الخدمة» or «ما حضر» (a pending one is rejected from its
  // details); the API's list says which of them this user may press.
  const quick = (b: Booking) =>
    canUpdate
      ? b.overdue
        ? OVERDUE_QUICK.filter((to) => b.allowedTransitions.includes(to))
        : QUICK.filter((to) => b.allowedTransitions.includes(to)).slice(0, 2)
      : [];

  // On the red card the secondary grey is below 4.5:1 (#5f6f80 on danger-50), so its rows' second line is darker.
  const rows = (bookings: Booking[], extra?: (b: Booking) => ReactNode, onDanger = false) => (
    <ul className="flex flex-col divide-y divide-border-row">
      {bookings.map((b) => (
        <li key={b.id} className="flex flex-col gap-2 py-3" data-testid="coming-appointment">
          <button
            type="button"
            onClick={() => open(b.id)}
            aria-label={tBoard('open', { name: b.customerName })}
            className="flex min-w-0 items-start gap-3 px-1 text-start hover:bg-bg-subtle focus-visible:shadow-[var(--focus-ring)]"
          >
            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
              <span className="text-label font-bold text-navy-900">
                {b.overdue
                  ? `${formatLocalDate(dayOf(b), locale, { weekday: 'long', day: 'numeric', month: 'long' }, calendar)} · ${formatTime(b.startsAt, locale, timeZone)}`
                  : t('timeRange', {
                      from: formatTime(b.startsAt, locale, timeZone),
                      to: formatTime(b.endsAt, locale, timeZone),
                    })}
              </span>
              <span className="flex flex-wrap items-center gap-2 text-body font-bold text-text-primary">
                {b.customerName}
                {b.loyaltyReward && <LoyaltyChip reward={b.loyaltyReward} />}
                <CustomerVisitBadge visit={b.visit} />
              </span>
              <span className={cn('text-helper', onDanger ? 'text-text-strong' : 'text-text-secondary')}>
                {localizedName(locale, b.item.nameAr, b.item.nameEn)} · {tChannel(bookingSource(b))}
                {showsBarber && !extra && ` · ${barberName(b)}`}
              </span>
              {b.customerPhone && (
                <span className="flex items-center gap-1.5 text-helper text-text-strong">
                  <Icon name="phone" className="size-3.5 shrink-0" />
                  <span dir="ltr" className="font-latin">
                    {b.customerPhone}
                  </span>
                </span>
              )}
              {extra?.(b)}
            </span>
            {b.overdue ? (
              <Badge tone="danger" size="sm">
                {t('overdueBadge')}
              </Badge>
            ) : (
              <StatusBadge kind="booking" status={b.status} size="sm" />
            )}
          </button>
          {quick(b).length > 0 && (
            <span className="flex flex-wrap gap-2 px-1">
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

  const chip = (key: string, label: string, target: Scope) => {
    const pressed =
      scope.kind === target.kind &&
      (target.kind !== 'barber' || (scope.kind === 'barber' && scope.id === target.id));
    return (
      <button
        key={key}
        type="button"
        aria-pressed={pressed}
        onClick={() => setScope(target)}
        className={cn(
          'min-h-9 rounded-full border-[1.5px] px-3.5 text-label font-semibold transition-colors focus-visible:shadow-[var(--focus-ring)] pointer-coarse:min-h-11',
          pressed
            ? 'border-navy-900 bg-navy-900 text-on-navy'
            : 'border-border-input bg-surface text-text-strong hover:border-brand-500',
        )}
      >
        {label}
      </button>
    );
  };

  return (
    <div className="flex max-w-[760px] flex-col gap-4" data-testid="simple-appointments">
      {quickActions && (
        <nav
          aria-label={t('quick.label')}
          className="grid grid-cols-2 gap-2 md:grid-cols-4"
          data-testid="quick-actions"
        >
          <ButtonLink href="/shop/walk-in" variant="primary" size="md" icon="user">
            {t('quick.walkIn')}
          </ButtonLink>
          <ButtonLink href="/shop/walk-in?kind=phone" variant="secondary" size="md" icon="phone">
            {t('quick.phone')}
          </ButtonLink>
          <ButtonLink href="/shop/schedule#breaks" variant="outline" size="md" icon="coffee">
            {t('quick.break')}
          </ButtonLink>
          <ButtonLink href="/shop/schedule#pause" variant="outline" size="md" icon="pause">
            {t('quick.pause')}
          </ButtonLink>
        </nav>
      )}

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
                  {t('responsible', { name: barberName(b) })}
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
            {rows(pending, (b) => (
              <span className="text-helper text-text-secondary">
                {formatLocalDate(
                  dayOf(b),
                  locale,
                  { weekday: 'long', day: 'numeric', month: 'long' },
                  calendar,
                )}{' '}
                — {barberName(b)}
              </span>
            ))}
          </>,
        )}

      <section
        className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1"
        aria-labelledby="coming-day-title"
        data-testid="coming-today"
      >
        <div className="flex items-center justify-between gap-2" data-testid="coming-day">
          <IconButton
            icon="chevL"
            label={t('previousDay')}
            variant="outline"
            disabled={day <= today}
            onClick={() => setDay(addDays(day, -1) < today ? today : addDays(day, -1))}
          />
          <h2 id="coming-day-title" className="min-w-0 text-center text-h3 font-bold text-navy-900">
            {day === today ? `${t('todayShort')} · ` : ''}
            {formatLocalDate(day, locale, { weekday: 'long', day: 'numeric', month: 'long' }, calendar)}
          </h2>
          <IconButton
            icon="chevR"
            label={t('nextDay')}
            variant="outline"
            onClick={() => setDay(addDays(day, 1))}
          />
        </div>
        {(ownProfessionalId || barbers.length > 1) && (
          <div role="group" aria-label={t('whose')} className="flex flex-wrap gap-2" data-testid="day-scope">
            {ownProfessionalId && chip('mine', t('mine'), { kind: 'mine' })}
            {chip('all', t('all'), { kind: 'all' })}
            {(ownProfessionalId ? colleagues : barbers).map((b) =>
              chip(b.id, localizedName(locale, b.nameAr, b.nameEn), { kind: 'barber', id: b.id }),
            )}
          </div>
        )}
        {list.isPending ? (
          <SkeletonList rows={4} label={tBoard('loading')} />
        ) : list.isError ? (
          <InlineAlert tone="danger" title={tBoard('loadError')} />
        ) : items.length === 0 ? (
          <p className="py-4 text-center text-caption text-text-secondary">
            {day === today ? t('noneToday') : t('noneOnDay')}
          </p>
        ) : (
          rows(items)
        )}
      </section>

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
                    {formatLocalDate(dayOf(b), locale, { day: 'numeric', month: 'long' }, calendar)} —{' '}
                    {formatTime(b.startsAt, locale, timeZone)} — {b.customerName}
                  </span>
                  <span className="text-helper text-text-secondary">
                    {t('closedLateBy', {
                      responsible: barberName(b),
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
