'use client';

import { useLocale, useTranslations } from 'next-intl';
import { cn } from '@/lib/cn';
import { OPERATING_TIME_ZONE } from '@/lib/i18n/config';
import { type AppLocale, formatDayNumber, formatTime } from '@/lib/i18n/format';
import {
  formatLocalDate,
  formatMonthYear,
  hourInZone,
  type LocalDate,
  monthDays,
  shiftMonth,
  toUtcNoon,
  weekday,
} from '@/lib/i18n/localDate';
import { IconButton } from './Button';
import { Icon } from './icons';

/* ---------------------------------------------------------------- Date strip */

export type DayAvailability = { date: LocalDate; available: boolean };

type DateStripProps = {
  name: string;
  days: DayAvailability[];
  value?: LocalDate;
  onValueChange: (date: LocalDate) => void;
  today?: LocalDate;
};

/**
 * Horizontal day picker for the booking wizard (design journey step, 14-day horizon). Native radios:
 * arrow keys move between bookable days; closed days are disabled and labelled.
 */
export function DateStrip({ name, days, value, onValueChange, today }: DateStripProps) {
  const t = useTranslations('ui.dateStrip');
  const locale = useLocale() as AppLocale;

  return (
    <fieldset className="min-w-0">
      <legend className="sr-only">{t('label')}</legend>
      <div className="-mx-4 flex [scrollbar-width:none] gap-2 overflow-x-auto px-4 pb-1">
        {days.map((day) => {
          const weekdayName =
            day.date === today ? t('today') : formatLocalDate(day.date, locale, { weekday: 'short' });
          return (
            <label
              key={day.date}
              className={cn(
                'relative flex min-h-[68px] w-[60px] shrink-0 cursor-pointer flex-col items-center justify-center gap-1 rounded-card border border-border-input bg-surface transition-colors',
                'has-checked:border-navy-900 has-checked:bg-navy-900 has-checked:text-on-navy has-focus-visible:shadow-[var(--focus-ring)]',
                !day.available && 'cursor-not-allowed border-border-row bg-bg-muted text-text-disabled',
              )}
            >
              <input
                type="radio"
                name={name}
                value={day.date}
                disabled={!day.available}
                checked={value === day.date}
                onChange={() => onValueChange(day.date)}
                aria-label={`${formatLocalDate(day.date, locale, { weekday: 'long', day: 'numeric', month: 'long' })}${day.available ? '' : ` — ${t('closed')}`}`}
                className="sr-only"
              />
              <span aria-hidden="true" className="text-badge font-medium">
                {weekdayName}
              </span>
              <span
                aria-hidden="true"
                className={cn('font-latin text-[1.0625rem] font-bold', !day.available && 'line-through')}
              >
                {formatDayNumber(toUtcNoon(day.date), locale, 'UTC')}
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}

/* ---------------------------------------------------------------- Slot grid */

export type Slot = { start: string };

const PERIODS = ['morning', 'afternoon', 'evening'] as const;
type Period = (typeof PERIODS)[number];

function periodOf(start: string, timeZone: string): Period {
  const hour = hourInZone(start, timeZone);
  if (hour < 12) return 'morning';
  if (hour < 17) return 'afternoon';
  return 'evening';
}

type SlotGridProps = {
  name: string;
  /** Only genuinely bookable slots, as returned by the availability API (D-009). */
  slots: Slot[];
  value?: string;
  onValueChange: (start: string) => void;
  timeZone?: string;
};

/**
 * Time-slot picker (design 491–511, 44px targets per DV-T05). Slots are grouped into morning /
 * afternoon / evening in the shop's time zone. There are no disabled "reason" slots: the API only
 * returns bookable times (D-009).
 */
export function SlotGrid({
  name,
  slots,
  value,
  onValueChange,
  timeZone = OPERATING_TIME_ZONE,
}: SlotGridProps) {
  const t = useTranslations('ui.slots');
  const locale = useLocale() as AppLocale;

  if (slots.length === 0) {
    return (
      <p
        role="status"
        className="rounded-card border border-border bg-bg-page px-4 py-5 text-center text-caption text-text-secondary"
      >
        {t('emptyDay')}
      </p>
    );
  }

  const grouped = PERIODS.map((period) => ({
    period,
    slots: slots.filter((slot) => periodOf(slot.start, timeZone) === period),
  })).filter((group) => group.slots.length > 0);

  return (
    <div role="radiogroup" aria-label={t('label')} className="flex flex-col gap-5">
      {grouped.map((group) => (
        <div
          key={group.period}
          role="group"
          aria-labelledby={`${name}-${group.period}`}
          className="flex flex-col gap-2.5"
        >
          <p
            id={`${name}-${group.period}`}
            className="text-eyebrow font-bold tracking-[0.1em] text-text-tertiary"
          >
            {t(group.period)}
          </p>
          <div className="grid grid-cols-[repeat(auto-fill,minmax(82px,1fr))] gap-2">
            {group.slots.map((slot) => (
              <label
                key={slot.start}
                className={cn(
                  'relative flex min-h-11 cursor-pointer items-center justify-center rounded-field border-[1.5px] border-border-input bg-surface text-[0.84375rem] font-bold text-text-strong transition-colors',
                  'hover:border-brand-500 has-checked:border-navy-900 has-checked:bg-navy-900 has-checked:text-on-navy has-focus-visible:shadow-[var(--focus-ring)]',
                )}
              >
                <input
                  type="radio"
                  name={name}
                  value={slot.start}
                  checked={value === slot.start}
                  onChange={() => onValueChange(slot.start)}
                  className="sr-only"
                />
                {formatTime(slot.start, locale, timeZone)}
              </label>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

/* ---------------------------------------------------------------- Stepper */

/** Booking progress (wizard): ordered list with the current step marked; mirrors in RTL naturally. */
export function Stepper({ steps, current }: { steps: string[]; current: number }) {
  const t = useTranslations('ui.stepper');
  const progress = steps.length > 1 ? (current / (steps.length - 1)) * 100 : 100;

  return (
    <nav aria-label={t('label')} className="flex flex-col gap-3">
      <p className="text-helper font-bold text-text-secondary">
        {t('progress', { current: current + 1, total: steps.length })}
      </p>
      <div aria-hidden="true" className="h-1 overflow-hidden rounded-full bg-bg-subtle">
        <div
          className="h-full rounded-full bg-navy-900 transition-[width] duration-300"
          style={{ width: `${progress}%` }}
        />
      </div>
      <ol className="hidden gap-2 md:flex">
        {steps.map((step, index) => {
          const state = index < current ? 'done' : index === current ? 'current' : 'todo';
          return (
            <li
              key={step}
              aria-current={state === 'current' ? 'step' : undefined}
              className={cn(
                'flex min-w-0 flex-1 items-center gap-2 text-helper',
                state === 'current'
                  ? 'font-bold text-navy-900'
                  : state === 'done'
                    ? 'text-text-strong'
                    : 'text-text-tertiary',
              )}
            >
              <span
                aria-hidden="true"
                className={cn(
                  'flex size-6 shrink-0 items-center justify-center rounded-full font-latin text-badge font-bold',
                  state === 'todo' ? 'bg-bg-subtle text-text-tertiary' : 'bg-navy-900 text-on-navy',
                )}
              >
                {state === 'done' ? <Icon name="check" className="size-3.5" strokeWidth={2.5} /> : index + 1}
              </span>
              <span className="truncate">{step}</span>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/* ---------------------------------------------------------------- Month calendar */

type CalendarMonthProps = {
  /** "YYYY-MM" currently shown. */
  month: string;
  onMonthChange: (month: string) => void;
  /** Days that can be picked; anything else (past, closed, outside the horizon) is disabled. */
  availableDates: ReadonlySet<LocalDate>;
  /** Explicitly closed days are shown struck through (design "مغلق"). */
  closedDates?: ReadonlySet<LocalDate>;
  value?: LocalDate;
  onValueChange: (date: LocalDate) => void;
  canGoBack?: boolean;
  canGoForward?: boolean;
};

/** Month date picker (design 467–489). Previous/next chevrons mirror in RTL. */
export function CalendarMonth({
  month,
  onMonthChange,
  availableDates,
  closedDates,
  value,
  onValueChange,
  canGoBack = true,
  canGoForward = true,
}: CalendarMonthProps) {
  const t = useTranslations('ui.calendar');
  const locale = useLocale() as AppLocale;
  const days = monthDays(month);
  const leadingBlanks = weekday(days[0]!);
  // Weekday headings, Sunday first (4 Jan 2026 is a Sunday).
  const weekdayNames = Array.from({ length: 7 }, (_, i) => {
    const date = new Date(Date.UTC(2026, 0, 4 + i, 12)).toISOString().slice(0, 10);
    return {
      short: formatLocalDate(date, locale, { weekday: 'short' }),
      long: formatLocalDate(date, locale, { weekday: 'long' }),
    };
  });

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h3 aria-live="polite" className="text-[0.9375rem] font-bold text-text-primary">
          {formatMonthYear(month, locale)}
        </h3>
        <div className="flex gap-1">
          <IconButton
            icon="chevL"
            label={t('previousMonth')}
            size="sm"
            variant="outline"
            disabled={!canGoBack}
            onClick={() => onMonthChange(shiftMonth(month, -1))}
          />
          <IconButton
            icon="chevR"
            label={t('nextMonth')}
            size="sm"
            variant="outline"
            disabled={!canGoForward}
            onClick={() => onMonthChange(shiftMonth(month, 1))}
          />
        </div>
      </div>
      <div className="grid grid-cols-7 gap-1 text-center">
        {weekdayNames.map((name) => (
          <abbr
            key={name.long}
            title={name.long}
            className="py-1 text-[0.6875rem] font-bold text-text-tertiary no-underline"
          >
            {name.short}
          </abbr>
        ))}
        {Array.from({ length: leadingBlanks }, (_, i) => (
          <span key={`blank-${i}`} aria-hidden="true" />
        ))}
        {days.map((date) => {
          const available = availableDates.has(date);
          const closed = closedDates?.has(date) ?? false;
          const selected = value === date;
          return (
            <button
              key={date}
              type="button"
              disabled={!available}
              aria-pressed={selected}
              aria-label={`${formatLocalDate(date, locale, { weekday: 'long', day: 'numeric', month: 'long' })}${closed ? ` — ${t('closed')}` : ''}`}
              onClick={() => onValueChange(date)}
              className={cn(
                'flex min-h-11 items-center justify-center rounded-sm font-latin text-label font-semibold transition-colors',
                selected && 'bg-navy-900 text-on-navy',
                !selected && available && 'bg-brand-100 text-brand-700 hover:bg-brand-150',
                !available && closed && 'bg-bg-muted text-decorative-200 line-through',
                !available && !closed && 'text-decorative-200',
              )}
            >
              {formatDayNumber(toUtcNoon(date), locale, 'UTC')}
            </button>
          );
        })}
      </div>
    </div>
  );
}
