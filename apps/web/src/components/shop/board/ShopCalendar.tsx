'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { SelectField } from '@/components/ui/inputs';
import { SegmentedControl } from '@/components/ui/selection.client';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import type { components } from '@/lib/api/schema';
import { cn } from '@/lib/cn';
import { type AppLocale, formatNumber, formatTime } from '@/lib/i18n/format';
import { addDays, formatLocalDate, hourInZone, weekday } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import {
  DAY_PARTS,
  type DayPart,
  dayAxis,
  dayPartOf,
  dayRange,
  gaps,
  heatLevel,
  type Interval,
  laneLayout,
  place,
  shortName,
} from '@/lib/shop/board';
import { useOperationsEvents } from '../live/OperationsLive';
import { AppointmentDrawer, BOARD_KEY } from './AppointmentDrawer';

type Calendar = components['schemas']['ShopCalendarResponse'];
type Day = components['schemas']['CalendarDayResponse'];
type Block = components['schemas']['CalendarBookingResponse'];

const HOUR_PX = 64;
const FREE_STEP_MINUTES = 30;

const blockTone: Record<string, string> = {
  Pending: 'border-status-pending-dot bg-status-pending-bg text-status-pending-fg',
  Confirmed: 'border-status-confirmed-dot bg-status-confirmed-bg text-status-confirmed-fg',
  Arrived: 'border-status-arrived-dot bg-status-arrived-bg text-status-arrived-fg',
  Completed: 'border-status-completed-dot bg-status-completed-bg text-status-completed-fg',
  NoShow: 'border-status-noshow-dot bg-status-noshow-bg text-status-noshow-fg',
};

const heatTone = {
  closed:
    'bg-[repeating-linear-gradient(45deg,var(--color-bg-muted)_0_6px,var(--color-surface)_6px_12px)] text-text-tertiary',
  none: 'bg-surface text-text-tertiary',
  low: 'bg-brand-100 text-navy-900',
  medium: 'bg-brand-300 text-navy-900',
  full: 'bg-navy-900 text-on-navy',
} as const;

const toInterval = (w: { start: string; end: string }): Interval => ({ start: w.start, end: w.end });
const asInterval = (b: Block): Interval & Block => ({ ...b, start: b.startsAt, end: b.endsAt });

/**
 * The shop calendar (s-calendar 2229–2303, D-034, DV-S19): a day view with one minute-accurate column per professional
 * (working time, breaks, time off, bookings, and free half-hours that open a prefilled walk-in), and a week view of
 * seven day columns with the bookings side by side plus the design's density strip. Bookings open the appointment
 * drawer. View, date and professional live in the URL; live changes refetch.
 */
export function ShopCalendar({
  today,
  timeZone,
  canWalkIn,
  canUpdate,
}: {
  today: string;
  timeZone: string;
  canWalkIn: boolean;
  canUpdate: boolean;
}) {
  const t = useTranslations('shopBoard.calendar');
  const locale = useLocale() as AppLocale;
  const params = useSearchParams();
  const queryClient = useQueryClient();
  // Free half-hours start from now; taken once per visit (live changes refetch the bookings).
  const [now] = useState(() => Date.now());

  const view = params.get('view') === 'week' ? 'week' : 'day';
  const date = /^\d{4}-\d{2}-\d{2}$/.test(params.get('date') ?? '') ? params.get('date')! : today;
  const professionalId = params.get('professional') ?? '';
  const bookingId = params.get('booking');
  const from = view === 'week' ? addDays(date, -weekday(date)) : date;
  const to = view === 'week' ? addDays(from, 6) : date;

  const update = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params.toString());
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    const query = next.toString();
    window.history.replaceState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}`);
  };

  const calendar = useQuery({
    queryKey: [BOARD_KEY, 'calendar', from, to, professionalId],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/calendar', {
          params: { query: { from, to, professionalId: professionalId || undefined } },
        }),
      ),
  });

  useOperationsEvents(() => {
    void queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
  });

  const step = view === 'week' ? 7 : 1;
  const title =
    view === 'week'
      ? t('weekOf', {
          from: formatLocalDate(from, locale, { day: 'numeric', month: 'long' }),
          to: formatLocalDate(to, locale, { day: 'numeric', month: 'long' }),
        })
      : formatLocalDate(date, locale, { weekday: 'long', day: 'numeric', month: 'long' });

  return (
    <div className="flex flex-col gap-4" data-testid="shop-calendar">
      <div className="flex flex-wrap items-end gap-3 rounded-card border border-border bg-surface p-4 shadow-e1">
        <SegmentedControl
          legend={t('view')}
          name="calendar-view"
          className="w-[200px]"
          value={view}
          onValueChange={(value) => update({ view: value === 'week' ? 'week' : null })}
          options={[
            { value: 'day', label: t('day') },
            { value: 'week', label: t('week') },
          ]}
        />
        <div className="flex items-center gap-1">
          <IconButton
            icon="chevL"
            label={t('previous')}
            variant="outline"
            onClick={() => update({ date: addDays(date, -step) })}
          />
          <h2
            aria-live="polite"
            className="min-w-[180px] px-2 text-center text-label font-bold text-text-primary"
            data-testid="calendar-title"
          >
            {title}
          </h2>
          <IconButton
            icon="chevR"
            label={t('next')}
            variant="outline"
            onClick={() => update({ date: addDays(date, step) })}
          />
          {date !== today && (
            <Button size="sm" variant="ghost" onClick={() => update({ date: null })}>
              {t('today')}
            </Button>
          )}
        </div>
        <SelectField
          label={t('professional')}
          className="min-w-[180px]"
          value={professionalId}
          onChange={(e) => update({ professional: e.target.value || null })}
        >
          <option value="">{t('allProfessionals')}</option>
          {(calendar.data?.professionals ?? []).map((p) => (
            <option key={p.id} value={p.id}>
              {localizedName(locale, p.nameAr, p.nameEn)}
            </option>
          ))}
        </SelectField>
        <ul
          className="ms-auto flex flex-wrap items-center gap-3 text-helper text-text-secondary"
          aria-label={t('legend')}
        >
          {(['Confirmed', 'Pending', 'Arrived', 'Completed'] as const).map((s) => (
            <li key={s} className="flex items-center gap-1.5">
              <span aria-hidden="true" className={cn('size-3 rounded-xs border-s-[3px]', blockTone[s])} />
              {t(`legendItems.${s}`)}
            </li>
          ))}
          <li className="flex items-center gap-1.5">
            <span aria-hidden="true" className={cn('size-3 rounded-xs', heatTone.closed)} />
            {t('legendItems.break')}
          </li>
        </ul>
      </div>

      {calendar.isPending ? (
        <SkeletonList rows={6} label={t('loading')} />
      ) : calendar.isError ? (
        <InlineAlert tone="danger" title={t('loadError')} />
      ) : view === 'day' ? (
        <DayView
          calendar={calendar.data}
          timeZone={timeZone}
          now={now}
          canWalkIn={canWalkIn}
          onOpen={(id) => update({ booking: id })}
        />
      ) : (
        <WeekView
          calendar={calendar.data}
          timeZone={timeZone}
          today={today}
          onOpen={(id) => update({ booking: id })}
          onDay={(d) => update({ view: null, date: d })}
        />
      )}

      <AppointmentDrawer
        bookingId={bookingId}
        timeZone={timeZone}
        canUpdate={canUpdate}
        onClose={() => update({ booking: null })}
      />
    </div>
  );
}

function Hours({
  axis,
  locale,
  timeZone,
  date,
}: {
  axis: { from: number; to: number };
  locale: AppLocale;
  timeZone: string;
  date: string;
}) {
  const range = dayRange(date, timeZone, axis);
  const hours = Array.from({ length: (axis.to - axis.from) / 60 }, (_, i) => range.start + i * 3_600_000);
  return (
    <div
      aria-hidden="true"
      className="relative w-16 shrink-0"
      style={{ height: ((axis.to - axis.from) / 60) * HOUR_PX }}
    >
      {hours.map((h, i) => (
        <span
          key={h}
          className="absolute start-0 -translate-y-1/2 text-[0.6875rem] font-medium text-text-tertiary"
          style={{ top: i * HOUR_PX }}
        >
          {formatTime(new Date(h).toISOString(), locale, timeZone)}
        </span>
      ))}
    </div>
  );
}

function Shade({
  interval,
  range,
  className,
  label,
}: {
  interval: Interval;
  range: { start: number; end: number };
  className: string;
  label?: string;
}) {
  const { top, height } = place(interval, range);
  if (height <= 0) return null;
  return (
    <div
      className={cn('absolute inset-x-0 overflow-hidden px-1.5 text-[0.6875rem] font-bold', className)}
      style={{ top: `${top}%`, height: `${height}%` }}
    >
      {label}
    </div>
  );
}

function BlockButton({
  block,
  range,
  locale,
  timeZone,
  lane = 0,
  lanes = 1,
  compact = false,
  onOpen,
}: {
  block: Block;
  range: { start: number; end: number };
  locale: AppLocale;
  timeZone: string;
  lane?: number;
  lanes?: number;
  compact?: boolean;
  onOpen: (id: string) => void;
}) {
  const t = useTranslations('shopBoard.calendar');
  const { top, height } = place({ start: block.startsAt, end: block.endsAt }, range);
  const item = localizedName(locale, block.itemNameAr, block.itemNameEn);
  const time = `${formatTime(block.startsAt, locale, timeZone)} — ${formatTime(block.endsAt, locale, timeZone)}`;
  return (
    <button
      type="button"
      onClick={() => onOpen(block.id)}
      aria-label={t('open', { name: block.customerName, item, time })}
      data-testid="calendar-booking"
      className={cn(
        'absolute z-10 flex flex-col overflow-hidden rounded-field border-s-[3px] px-1.5 py-0.5 text-start shadow-e1 hover:brightness-95 focus-visible:shadow-[var(--focus-ring)]',
        blockTone[block.status] ?? blockTone.Confirmed,
      )}
      style={{
        top: `${top}%`,
        height: `max(${height}%, 22px)`,
        insetInlineStart: `calc(${(lane / lanes) * 100}% + 2px)`,
        width: `calc(${100 / lanes}% - 4px)`,
      }}
    >
      <span className="truncate text-[0.75rem] font-bold">{shortName(block.customerName)}</span>
      {!compact && <span className="truncate text-[0.6875rem]">{item}</span>}
    </button>
  );
}

function DayView({
  calendar,
  timeZone,
  now,
  canWalkIn,
  onOpen,
}: {
  calendar: Calendar;
  timeZone: string;
  now: number;
  canWalkIn: boolean;
  onOpen: (id: string) => void;
}) {
  const t = useTranslations('shopBoard.calendar');
  const locale = useLocale() as AppLocale;
  const day: Day = calendar.days[0]!;
  const axis = dayAxis(
    [{ date: day.date, intervals: [...day.open.map(toInterval), ...day.bookings.map(asInterval)] }],
    timeZone,
  );
  const range = dayRange(day.date, timeZone, axis);
  const height = ((axis.to - axis.from) / 60) * HOUR_PX;

  if (calendar.professionals.length === 0) {
    return <InlineAlert tone="info" title={t('noProfessionals')} />;
  }

  return (
    <div className="rounded-card border border-border bg-surface p-4 shadow-e1">
      {day.closed && <InlineAlert tone="warning" title={t('closed')} className="mb-3" />}
      <div className="flex [scroll-snap-type:x_mandatory] gap-2 overflow-x-auto" data-testid="calendar-day">
        <div className="sticky start-0 z-20 shrink-0 bg-surface pt-12">
          <Hours axis={axis} locale={locale} timeZone={timeZone} date={day.date} />
        </div>
        {calendar.professionals.map((p) => {
          const lane = day.lanes.find((l) => l.professionalId === p.id);
          const working = (lane?.working ?? []).map(toInterval);
          const bookings = day.bookings.filter((b) => b.professionalId === p.id);
          const busy = [
            ...bookings.map(asInterval),
            ...(lane?.breaks ?? []).map(toInterval),
            ...(lane?.timeOff ?? []).map(toInterval),
          ];
          const name = localizedName(locale, p.nameAr, p.nameEn);
          const free: number[] = [];
          for (const w of working) {
            for (
              let s = new Date(w.start).getTime();
              s + FREE_STEP_MINUTES * 60_000 <= new Date(w.end).getTime();
              s += FREE_STEP_MINUTES * 60_000
            ) {
              const e = s + FREE_STEP_MINUTES * 60_000;
              if (
                s >= now &&
                !busy.some((b) => new Date(b.start).getTime() < e && s < new Date(b.end).getTime())
              )
                free.push(s);
            }
          }
          return (
            <section
              key={p.id}
              aria-label={name}
              className="w-[82vw] shrink-0 snap-start sm:w-auto sm:min-w-[180px] sm:flex-1"
              data-testid="calendar-column"
            >
              <header className="flex h-12 flex-col justify-center border-b border-border px-2">
                <h3 className="truncate text-label font-bold text-text-primary">{name}</h3>
                <p className="text-badge text-text-secondary">{t('count', { count: bookings.length })}</p>
              </header>
              <div className="relative border-s border-border-row" style={{ height }}>
                {Array.from({ length: (axis.to - axis.from) / 60 }, (_, i) => (
                  <span
                    key={i}
                    aria-hidden="true"
                    className="absolute inset-x-0 border-t border-dashed border-border-row"
                    style={{ top: i * HOUR_PX }}
                  />
                ))}
                {gaps(working, range).map((g) => (
                  <Shade
                    key={`off-${g.start}`}
                    interval={g}
                    range={range}
                    className="bg-bg-muted text-text-tertiary"
                    label={t('offShift')}
                  />
                ))}
                {(lane?.breaks ?? []).map((b) => (
                  <Shade
                    key={`break-${b.start}`}
                    interval={b}
                    range={range}
                    className={heatTone.closed}
                    label={t('break')}
                  />
                ))}
                {(lane?.timeOff ?? []).map((b) => (
                  <Shade
                    key={`off-${b.start}`}
                    interval={b}
                    range={range}
                    className="bg-warning-50 text-warning-700"
                    label={t('timeOff')}
                  />
                ))}
                {canWalkIn &&
                  free.map((s) => {
                    const at = new Date(s).toISOString();
                    const { top, height: h } = place(
                      { start: at, end: new Date(s + FREE_STEP_MINUTES * 60_000).toISOString() },
                      range,
                    );
                    return (
                      <Link
                        key={s}
                        href={`/shop/walk-in?professionalId=${p.id}&startsAt=${encodeURIComponent(at)}&date=${day.date}`}
                        aria-label={t('walkInAt', { name, time: formatTime(at, locale, timeZone) })}
                        className="group absolute inset-x-1 flex items-center justify-center rounded-field border border-dashed border-transparent text-[0.6875rem] font-bold text-transparent hover:border-brand-500 hover:text-brand-700 focus-visible:border-brand-500 focus-visible:text-brand-700"
                        style={{ top: `${top}%`, height: `${h}%` }}
                      >
                        + {t('walkIn')}
                      </Link>
                    );
                  })}
                {bookings.map((b) => (
                  <BlockButton
                    key={b.id}
                    block={b}
                    range={range}
                    locale={locale}
                    timeZone={timeZone}
                    onOpen={onOpen}
                  />
                ))}
              </div>
            </section>
          );
        })}
      </div>
    </div>
  );
}

function WeekView({
  calendar,
  timeZone,
  today,
  onOpen,
  onDay,
}: {
  calendar: Calendar;
  timeZone: string;
  today: string;
  onOpen: (id: string) => void;
  onDay: (date: string) => void;
}) {
  const t = useTranslations('shopBoard.calendar');
  const locale = useLocale() as AppLocale;
  const axis = dayAxis(
    calendar.days.map((d) => ({
      date: d.date,
      intervals: [...d.open.map(toInterval), ...d.bookings.map(asInterval)],
    })),
    timeZone,
  );
  const height = ((axis.to - axis.from) / 60) * HOUR_PX;
  const density = calendar.days.map((d) => {
    const counts = Object.fromEntries(DAY_PARTS.map((part) => [part, 0])) as Record<DayPart, number>;
    for (const b of d.bookings) counts[dayPartOf(hourInZone(b.startsAt, timeZone))] += 1;
    return { date: d.date, closed: d.open.length === 0, counts };
  });

  return (
    <div className="flex flex-col gap-4">
      <section
        aria-labelledby="density-title"
        className="overflow-x-auto rounded-card border border-border bg-surface p-4 shadow-e1"
      >
        <h3 id="density-title" className="pb-3 text-label font-bold text-text-primary">
          {t('density')}
        </h3>
        <table
          className="w-full min-w-[560px] border-separate border-spacing-1 text-center"
          data-testid="calendar-density"
        >
          <thead>
            <tr>
              <td />
              {density.map((d) => (
                <th
                  key={d.date}
                  scope="col"
                  className={cn(
                    'rounded-field py-1 text-helper font-bold',
                    d.date === today ? 'bg-brand-100 text-navy-900' : 'text-text-secondary',
                  )}
                >
                  <button type="button" onClick={() => onDay(d.date)} className="hover:underline">
                    {formatLocalDate(d.date, locale, { weekday: 'short', day: 'numeric' })}
                  </button>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {DAY_PARTS.map((part) => (
              <tr key={part}>
                <th scope="row" className="pe-2 text-start text-helper font-medium text-text-secondary">
                  {t(`parts.${part}`)}
                </th>
                {density.map((d) => {
                  const level = heatLevel(d.closed ? null : d.counts[part]);
                  return (
                    <td
                      key={d.date}
                      className={cn('h-9 rounded-field font-latin text-label font-bold', heatTone[level])}
                    >
                      {level === 'closed' ? (
                        <span aria-label={t('closedCell')}>—</span>
                      ) : (
                        formatNumber(d.counts[part], locale)
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <div className="rounded-card border border-border bg-surface p-4 shadow-e1">
        <div className="flex gap-2 overflow-x-auto" data-testid="calendar-week">
          <div className="sticky start-0 z-20 shrink-0 bg-surface pt-12">
            <Hours axis={axis} locale={locale} timeZone={timeZone} date={calendar.days[0]!.date} />
          </div>
          {calendar.days.map((d) => {
            const range = dayRange(d.date, timeZone, axis);
            return (
              <section
                key={d.date}
                aria-label={formatLocalDate(d.date, locale, {
                  weekday: 'long',
                  day: 'numeric',
                  month: 'long',
                })}
                className="min-w-[120px] flex-1"
              >
                <header
                  className={cn(
                    'flex h-12 flex-col justify-center border-b border-border px-2',
                    d.date === today && 'bg-brand-50',
                  )}
                >
                  <button
                    type="button"
                    onClick={() => onDay(d.date)}
                    className="text-start text-label font-bold text-text-primary hover:underline"
                  >
                    {formatLocalDate(d.date, locale, { weekday: 'short', day: 'numeric' })}
                  </button>
                  <p className="text-badge text-text-secondary">
                    {d.open.length === 0 ? t('closedShort') : t('count', { count: d.bookings.length })}
                  </p>
                </header>
                <div className="relative border-s border-border-row" style={{ height }}>
                  {gaps(d.open.map(toInterval), range).map((g) => (
                    <Shade key={`closed-${g.start}`} interval={g} range={range} className="bg-bg-muted" />
                  ))}
                  {laneLayout(d.bookings.map(asInterval)).map(({ item, lane, lanes }) => (
                    <BlockButton
                      key={item.id}
                      block={item}
                      range={range}
                      locale={locale}
                      timeZone={timeZone}
                      lane={lane}
                      lanes={lanes}
                      compact
                      onOpen={onOpen}
                    />
                  ))}
                </div>
              </section>
            );
          })}
        </div>
      </div>
    </div>
  );
}
