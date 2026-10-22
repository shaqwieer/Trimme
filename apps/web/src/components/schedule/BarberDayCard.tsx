'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useRef, useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { SelectField } from '@/components/ui/inputs';
import { ConfirmDialog } from '@/components/ui/overlays';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { cn } from '@/lib/cn';
import { type AppLocale, formatDurationMinutes } from '@/lib/i18n/format';
import { addDays, formatLocalDate, type LocalDate } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import {
  addBreaks,
  type BarberDay,
  cancellableBreak,
  clearDay,
  freeStarts,
  hasBreaksAhead,
  type HourState,
  hourCells,
  minuteCells,
  type MinuteWindow,
  removeBreak,
  type SlotState,
  slotsToWindows,
  wholeDay,
} from '@/lib/shop/barberDay';
import { formatMinutes } from './time';

export type BreakBarber = { id: string; nameAr: string; nameEn: string };

const KEY = 'barber-day';

const CELL: Record<HourState, string> = {
  free: 'border-border-input bg-surface text-text-primary hover:border-brand-500',
  partFree: 'border-border-input bg-surface text-text-primary hover:border-brand-500',
  break: 'border-navy-900 bg-navy-900 text-on-navy',
  partBreak: 'border-navy-900 bg-surface text-navy-900 hover:border-brand-500',
  shopBreak: 'border-border bg-bg-muted text-text-secondary',
  booked: 'border-status-confirmed-dot bg-status-confirmed-bg text-status-confirmed-fg',
  off: 'border-border bg-bg-muted text-text-tertiary',
  outside: 'border-dashed border-border-dashed bg-bg-page text-text-tertiary',
};

const SELECTED = 'border-brand-700 bg-brand-100 text-brand-700';

/** What a confirmation is about; each one saves the barber's breaks for the day once confirmed. */
type Pending =
  | { kind: 'add'; windows: MinuteWindow[] }
  | { kind: 'remove'; window: MinuteWindow }
  | { kind: 'wholeDay' }
  | { kind: 'clear' };

/**
 * A barber's breaks for one day (D-134, the client's «البريكات»), every 5 minutes (D-137): the day's 24 hours; tapping
 * one opens its twelve 5-minute slots, as the booking's hour and minutes do. Free slots are picked and then confirmed
 * as a break; tapping a break offers to cancel it. Every change asks first and then says what was saved. No reason is
 * asked and nothing repeats. Booked time, the owner's breaks and time off cannot be changed here.
 */
export function BarberDayCard({ barbers, today }: { barbers: BreakBarber[]; today: LocalDate }) {
  const t = useTranslations('shopSchedule.barberDay');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const queryClient = useQueryClient();
  const panelId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const [barberId, setBarberId] = useState(barbers[0]?.id ?? '');
  const [date, setDate] = useState(today);
  const [openHour, setOpenHour] = useState<number | null>(null);
  const [picked, setPicked] = useState<number[]>([]);
  const [pending, setPending] = useState<Pending | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [done, setDone] = useState<string>();

  const day = useQuery({
    queryKey: [KEY, barberId, date],
    enabled: Boolean(barberId),
    placeholderData: keepPreviousData,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/schedule/barber-day', {
          params: { query: { professionalId: barberId, date } },
        }),
      ),
  });

  // Bring the minutes into view on a phone, where they open below the hours.
  useEffect(() => {
    if (openHour !== null) panelRef.current?.scrollIntoView?.({ block: 'nearest' });
  }, [openHour]);

  if (barbers.length === 0) return <EmptyState icon="users" title={t('noBarbers')} />;

  const barber = barbers.find((b) => b.id === barberId);
  const barberName = barber ? localizedName(locale, barber.nameAr, barber.nameEn) : '';
  const current =
    day.data && day.data.date === date && day.data.professionalId === barberId ? day.data : undefined;
  const cells = current ? hourCells(current) : [];
  const time = (minutes: number) => formatMinutes(minutes, locale);
  const range = (w: MinuteWindow) => t('range', { from: time(w.startMinute), to: time(w.endMinute) });
  const ranges = (windows: MinuteWindow[]) =>
    new Intl.ListFormat(locale, { type: 'conjunction' }).format(windows.map(range));

  const reset = () => {
    setOpenHour(null);
    setPicked([]);
    setError(undefined);
    setDone(undefined);
  };

  const breaksFor = (on: BarberDay, change: Pending): MinuteWindow[] => {
    switch (change.kind) {
      case 'add':
        return addBreaks(on, change.windows);
      case 'remove':
        return removeBreak(on, change.window);
      case 'wholeDay':
        return wholeDay(on);
      case 'clear':
        return clearDay(on);
    }
  };

  const doneText = (change: Pending) => {
    switch (change.kind) {
      case 'add':
        return t('done.add', { when: ranges(change.windows), barber: barberName });
      case 'remove':
        return t('done.remove', { when: range(change.window) });
      case 'wholeDay':
        return t('done.wholeDay', { barber: barberName });
      case 'clear':
        return t('done.clear', { barber: barberName });
    }
  };

  const save = async (change: Pending) => {
    if (!current) return;
    setError(undefined);
    setDone(undefined);
    setSaving(true);
    try {
      const next = ensureOk(
        await browserApi.PUT('/api/v1/shop/schedule/barber-day', {
          body: { professionalId: barberId, date, breaks: breaksFor(current, change) },
        }),
      );
      queryClient.setQueryData([KEY, barberId, date], next);
      setPicked([]);
      setDone(doneText(change));
    } catch (failure) {
      const tooMany =
        failure instanceof ApiError && failure.fieldErrors.breaks?.includes('validation.too_many');
      setError(
        tooMany ? t('tooMany') : apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'),
      );
      await queryClient.invalidateQueries({ queryKey: [KEY, barberId, date] });
    } finally {
      setSaving(false);
      setPending(null);
    }
  };

  const confirmText = pending
    ? pending.kind === 'add'
      ? {
          title: t('confirm.add.title', { when: ranges(pending.windows), barber: barberName }),
          key: 'add' as const,
        }
      : pending.kind === 'remove'
        ? { title: t('confirm.remove.title', { when: range(pending.window) }), key: 'remove' as const }
        : pending.kind === 'wholeDay'
          ? { title: t('confirm.wholeDay.title', { barber: barberName }), key: 'wholeDay' as const }
          : { title: t('confirm.clear.title', { barber: barberName }), key: 'clear' as const }
    : undefined;

  const hour = openHour !== null && current ? cells[openHour] : undefined;
  const slots = hour && current ? minuteCells(current, hour.hour) : [];
  const freeSlots = slots.filter((s) => s.toggles && s.state === 'free').map((s) => s.start);
  const allPicked = freeSlots.length > 0 && freeSlots.every((start) => picked.includes(start));
  const stateLabel = (state: HourState | SlotState, breakMinutes = 0) =>
    state === 'partBreak'
      ? t('state.partBreak', { duration: formatDurationMinutes(breakMinutes, locale) })
      : t(`state.${state}`);

  return (
    <section
      className="flex flex-col gap-4 rounded-card border border-border bg-surface p-5 shadow-e1"
      data-testid="barber-day"
      aria-busy={saving || day.isFetching}
    >
      <div className="flex flex-col gap-1">
        <h2 className="text-h3 font-bold text-text-primary">{t('title')}</h2>
        <p className="text-caption text-text-secondary">{t('body')}</p>
      </div>

      {barbers.length > 1 ? (
        <SelectField
          label={t('barber')}
          value={barberId}
          onChange={(event) => {
            setBarberId(event.target.value);
            reset();
          }}
        >
          {barbers.map((b) => (
            <option key={b.id} value={b.id}>
              {localizedName(locale, b.nameAr, b.nameEn)}
            </option>
          ))}
        </SelectField>
      ) : (
        barber && <p className="text-label font-bold text-text-strong">{barberName}</p>
      )}

      <div className="flex items-center justify-between gap-2" data-testid="barber-day-date">
        <IconButton
          icon="chevL"
          label={t('previous')}
          variant="outline"
          disabled={date <= today}
          onClick={() => {
            setDate(addDays(date, -1));
            reset();
          }}
        />
        <div className="flex min-w-0 flex-col items-center text-center">
          <span className="text-label font-bold text-text-primary">
            {date === today ? t('today') : formatLocalDate(date, locale, { weekday: 'long' })}
          </span>
          <span className="text-helper text-text-secondary">
            {formatLocalDate(date, locale, { day: 'numeric', month: 'long' })}
          </span>
        </div>
        <IconButton
          icon="chevR"
          label={t('next')}
          variant="outline"
          onClick={() => {
            setDate(addDays(date, 1));
            reset();
          }}
        />
      </div>

      {!current ? (
        day.isError ? (
          <InlineAlert tone="danger" title={t('loadError')} />
        ) : (
          <SkeletonList rows={4} label={t('loading')} />
        )
      ) : (
        <>
          {current.working.length === 0 && (
            <p className="rounded-card bg-bg-muted p-3 text-helper text-text-secondary">{t('dayOff')}</p>
          )}
          <ul
            className="grid grid-cols-[repeat(auto-fill,minmax(6rem,1fr))] gap-2"
            data-testid="barber-day-hours"
          >
            {cells.map((cell) => (
              <li key={cell.hour}>
                <button
                  type="button"
                  data-hour={cell.hour}
                  data-state={cell.state}
                  aria-expanded={cell.opens ? openHour === cell.hour : undefined}
                  aria-controls={cell.opens && openHour === cell.hour ? panelId : undefined}
                  aria-label={t('cell', {
                    from: time(cell.start),
                    to: time(cell.end),
                    state: stateLabel(cell.state, cell.breakMinutes),
                  })}
                  disabled={!cell.opens || saving}
                  onClick={() => {
                    setOpenHour(openHour === cell.hour ? null : cell.hour);
                    setPicked([]);
                    setDone(undefined);
                    setError(undefined);
                  }}
                  className={cn(
                    'flex min-h-14 w-full flex-col items-center justify-center gap-0.5 rounded-button border-[1.5px] px-1 py-1.5 text-center transition-colors focus-visible:shadow-[var(--focus-ring)] disabled:cursor-default',
                    CELL[cell.state],
                    cell.past && 'opacity-50',
                    openHour === cell.hour && 'ring-2 ring-brand-500 ring-offset-2 ring-offset-surface',
                  )}
                >
                  <span className="text-label font-bold">{time(cell.start)}</span>
                  <span className="text-helper">{stateLabel(cell.state, cell.breakMinutes)}</span>
                </button>
              </li>
            ))}
          </ul>

          {hour && (
            <div
              ref={panelRef}
              id={panelId}
              role="group"
              aria-labelledby={`${panelId}-title`}
              className="flex scroll-mt-24 scroll-mb-[calc(var(--layout-bottom-nav-height)+1rem)] flex-col gap-3 rounded-card border border-border bg-bg-page p-4 lg:scroll-mb-0"
              data-testid="barber-day-minutes"
            >
              <div className="flex items-center justify-between gap-2">
                <h3 id={`${panelId}-title`} className="text-label font-bold text-text-primary">
                  {t('range', { from: time(hour.start), to: time(hour.end) })}
                </h3>
                <IconButton icon="x" label={t('closeHour')} variant="ghost" onClick={() => reset()} />
              </div>
              <p className="text-helper text-text-secondary">{t('minutesHint')}</p>
              <ul className="grid grid-cols-[repeat(auto-fill,minmax(5rem,1fr))] gap-2">
                {slots.map((slot) => {
                  const isPicked = picked.includes(slot.start);
                  const label = isPicked ? t('selected') : stateLabel(slot.state);
                  return (
                    <li key={slot.start}>
                      <button
                        type="button"
                        data-minute={slot.start}
                        data-state={slot.state}
                        aria-pressed={slot.toggles && slot.state === 'free' ? isPicked : undefined}
                        aria-label={t('slot', { time: time(slot.start), state: label })}
                        disabled={!slot.toggles || saving}
                        onClick={() => {
                          setDone(undefined);
                          if (slot.state === 'break') {
                            const window = cancellableBreak(current, slot.start);
                            if (window) setPending({ kind: 'remove', window });
                            return;
                          }
                          setPicked(
                            isPicked ? picked.filter((s) => s !== slot.start) : [...picked, slot.start],
                          );
                        }}
                        className={cn(
                          'flex min-h-12 w-full flex-col items-center justify-center gap-0.5 rounded-field border-[1.5px] px-1 py-1 text-center transition-colors focus-visible:shadow-[var(--focus-ring)] disabled:cursor-default',
                          isPicked ? SELECTED : CELL[slot.state],
                          slot.past && 'opacity-50',
                        )}
                      >
                        <span className="text-[0.84375rem] font-bold">{time(slot.start)}</span>
                        <span className="flex items-center gap-1 text-[0.6875rem] font-medium">
                          {isPicked && <Icon name="check" className="size-3" strokeWidth={2.5} />}
                          {label}
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
              <div className="flex flex-wrap gap-2">
                <Button
                  variant="outline"
                  size="md"
                  disabled={saving || freeSlots.length === 0}
                  onClick={() => setPicked(allPicked ? [] : freeSlots)}
                >
                  {allPicked ? t('clearSelection') : t('wholeHour')}
                </Button>
                <Button
                  size="md"
                  icon="check"
                  disabled={saving || picked.length === 0}
                  onClick={() => setPending({ kind: 'add', windows: slotsToWindows(picked) })}
                >
                  {t('confirmBreak')}
                </Button>
              </div>
            </div>
          )}

          {done && <InlineAlert tone="success" title={done} />}
          {error && <InlineAlert tone="danger" title={error} />}

          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              size="md"
              disabled={saving || freeStarts(current).length === 0}
              onClick={() => setPending({ kind: 'wholeDay' })}
            >
              {t('wholeDay')}
            </Button>
            <Button
              variant="ghost"
              size="md"
              disabled={saving || !hasBreaksAhead(current)}
              onClick={() => setPending({ kind: 'clear' })}
            >
              {t('clear')}
            </Button>
          </div>
        </>
      )}

      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(open) => !open && !saving && setPending(null)}
        tone={pending?.kind === 'add' || pending?.kind === 'wholeDay' ? 'default' : 'danger'}
        title={confirmText?.title ?? ''}
        body={confirmText ? t(`confirm.${confirmText.key}.body`) : ''}
        confirmLabel={confirmText ? t(`confirm.${confirmText.key}.action`) : ''}
        cancelLabel={t('confirm.back')}
        loading={saving}
        onConfirm={() => pending && void save(pending)}
      />
    </section>
  );
}
