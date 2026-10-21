'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { Button, IconButton } from '@/components/ui/Button';
import { SelectField } from '@/components/ui/inputs';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { cn } from '@/lib/cn';
import type { AppLocale } from '@/lib/i18n/format';
import { addDays, formatLocalDate, type LocalDate } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import {
  clearDay,
  type HourState,
  hourCells,
  type MinuteWindow,
  toggleHour,
  wholeDay,
} from '@/lib/shop/barberDay';
import { formatMinutes } from './time';

export type BreakBarber = { id: string; nameAr: string; nameEn: string };

const KEY = 'barber-day';

const CELL: Record<HourState, string> = {
  free: 'border-border-input bg-surface text-text-primary hover:border-brand-500',
  break: 'border-navy-900 bg-navy-900 text-on-navy',
  shopBreak: 'border-border bg-bg-muted text-text-secondary',
  booked: 'border-status-confirmed-dot bg-status-confirmed-bg text-status-confirmed-fg',
  off: 'border-border bg-bg-muted text-text-tertiary',
  outside: 'border-dashed border-border-dashed bg-bg-page text-text-tertiary',
};

/**
 * A barber's breaks for one day (D-134, the client's «البريكات»): the day's 24 hours, the barber's working hours
 * among them; tapping an hour makes it a break and tapping again frees it. No reason is asked and nothing repeats. Each
 * tap saves at once. Booked hours, the owner's breaks and time off cannot be changed here.
 */
export function BarberDayCard({ barbers, today }: { barbers: BreakBarber[]; today: LocalDate }) {
  const t = useTranslations('shopSchedule.barberDay');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const queryClient = useQueryClient();
  const [barberId, setBarberId] = useState(barbers[0]?.id ?? '');
  const [date, setDate] = useState(today);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [saved, setSaved] = useState(false);

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

  if (barbers.length === 0) return <EmptyState icon="users" title={t('noBarbers')} />;

  const save = async (breaks: MinuteWindow[]) => {
    setError(undefined);
    setSaved(false);
    setSaving(true);
    try {
      const next = ensureOk(
        await browserApi.PUT('/api/v1/shop/schedule/barber-day', {
          body: { professionalId: barberId, date, breaks },
        }),
      );
      queryClient.setQueryData([KEY, barberId, date], next);
      setSaved(true);
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
      await queryClient.invalidateQueries({ queryKey: [KEY, barberId, date] });
    } finally {
      setSaving(false);
    }
  };

  const go = (next: LocalDate) => {
    setDate(next);
    setError(undefined);
    setSaved(false);
  };
  const barber = barbers.find((b) => b.id === barberId);
  const current =
    day.data && day.data.date === date && day.data.professionalId === barberId ? day.data : undefined;
  const cells = current ? hourCells(current) : [];

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
            setError(undefined);
            setSaved(false);
          }}
        >
          {barbers.map((b) => (
            <option key={b.id} value={b.id}>
              {localizedName(locale, b.nameAr, b.nameEn)}
            </option>
          ))}
        </SelectField>
      ) : (
        barber && (
          <p className="text-label font-bold text-text-strong">
            {localizedName(locale, barber.nameAr, barber.nameEn)}
          </p>
        )
      )}

      <div className="flex items-center justify-between gap-2" data-testid="barber-day-date">
        <IconButton
          icon="chevL"
          label={t('previous')}
          variant="outline"
          disabled={date <= today}
          onClick={() => go(addDays(date, -1))}
        />
        <div className="flex min-w-0 flex-col items-center text-center">
          <span className="text-label font-bold text-text-primary">
            {date === today ? t('today') : formatLocalDate(date, locale, { weekday: 'long' })}
          </span>
          <span className="text-helper text-text-secondary">
            {formatLocalDate(date, locale, { day: 'numeric', month: 'long' })}
          </span>
        </div>
        <IconButton icon="chevR" label={t('next')} variant="outline" onClick={() => go(addDays(date, 1))} />
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
                  aria-pressed={cell.toggles ? cell.state === 'break' : undefined}
                  aria-label={t('cell', {
                    from: formatMinutes(cell.start, locale),
                    to: formatMinutes(cell.end, locale),
                    state: t(`state.${cell.state}`),
                  })}
                  disabled={!cell.toggles || saving}
                  onClick={() => void save(toggleHour(current, cell))}
                  className={cn(
                    'flex min-h-14 w-full flex-col items-center justify-center gap-0.5 rounded-button border-[1.5px] px-1 py-1.5 text-center transition-colors focus-visible:shadow-[var(--focus-ring)] disabled:cursor-default',
                    CELL[cell.state],
                    cell.past && 'opacity-50',
                  )}
                >
                  <span className="text-label font-bold">{formatMinutes(cell.start, locale)}</span>
                  <span className="text-helper">{t(`state.${cell.state}`)}</span>
                </button>
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap gap-2">
            <Button
              variant="outline"
              size="md"
              disabled={saving || !cells.some((c) => c.toggles && c.state === 'free')}
              onClick={() => void save(wholeDay(current))}
            >
              {t('wholeDay')}
            </Button>
            <Button
              variant="ghost"
              size="md"
              disabled={saving || !cells.some((c) => c.toggles && c.state === 'break')}
              onClick={() => void save(clearDay(current))}
            >
              {t('clear')}
            </Button>
          </div>
        </>
      )}

      {error && <InlineAlert tone="danger" title={error} />}
      <p role="status" className="sr-only">
        {saved && !saving ? t('saved') : ''}
      </p>
    </section>
  );
}
