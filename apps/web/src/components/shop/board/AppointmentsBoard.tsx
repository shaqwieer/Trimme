'use client';

import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { SearchField, SelectField, TextField } from '@/components/ui/inputs';
import { Chip } from '@/components/ui/selection.client';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk } from '@/lib/api/errors';
import type { components } from '@/lib/api/schema';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatNumber,
  formatTime,
} from '@/lib/i18n/format';
import { addDays } from '@/lib/i18n/localDate';
import { localizedName } from '@/lib/i18n/localized';
import { chipOf, STATUS_CHIPS, type StatusChip, statusesOf } from '@/lib/shop/board';
import { useOperationsEvents } from '../live/OperationsLive';
import { AppointmentDrawer, BOARD_KEY } from './AppointmentDrawer';

type Counts = components['schemas']['ShopBookingCounts'];

export type BoardProfessional = { id: string; nameAr: string; nameEn: string };

const PAGE_SIZE = 20;

/**
 * The appointments list (s-appointments 2305–2395, DV-S08/S18): status chips with counts (every status; cancellations
 * together), a date range, a professional, search by customer name or booking reference only (never a phone), a table
 * that becomes cards on phones, paging, and the detail drawer (`?booking=` keeps it linkable). Filters live in the URL;
 * live changes refetch the page.
 */
export function AppointmentsBoard({
  today,
  timeZone,
  professionals,
  canUpdate,
}: {
  today: string;
  timeZone: string;
  professionals: BoardProfessional[];
  canUpdate: boolean;
}) {
  const t = useTranslations('shopBoard.appointments');
  const tDrawer = useTranslations('shopBoard.drawer');
  const locale = useLocale() as AppLocale;
  const params = useSearchParams();
  const queryClient = useQueryClient();

  const chip = chipOf(params.get('status'));
  const from = params.get('from') ?? today;
  const to = params.get('to') ?? addDays(today, 6);
  const professionalId = params.get('professional') ?? '';
  const search = params.get('q') ?? '';
  const page = Math.max(1, Number(params.get('page')) || 1);
  const bookingId = params.get('booking');
  const [draft, setDraft] = useState(search);

  const update = (changes: Record<string, string | null>, resetPage = true) => {
    const next = new URLSearchParams(params.toString());
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    if (resetPage && !('page' in changes)) next.delete('page');
    const query = next.toString();
    window.history.replaceState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}`);
  };

  const list = useQuery({
    queryKey: [BOARD_KEY, 'list', { chip, from, to, professionalId, search, page }],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/bookings', {
          params: {
            query: {
              from,
              to,
              status: statusesOf(chip),
              professionalId: professionalId || undefined,
              search: search || undefined,
              page,
              pageSize: PAGE_SIZE,
            },
          },
        }),
      ),
  });

  useOperationsEvents(() => {
    void queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
  });

  const counts: Counts | undefined = list.data?.counts;
  const total = list.data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="flex flex-col gap-4" data-testid="appointments-board">
      <div className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1">
        <div role="group" aria-label={t('statusFilter')} className="flex flex-wrap gap-2">
          {STATUS_CHIPS.map((c: StatusChip) => (
            <Chip
              key={c}
              pressed={chip === c}
              onPressedChange={() => update({ status: c === 'all' ? null : c })}
            >
              {t(`chips.${c}`)}
              {counts && <span className="font-latin"> · {formatNumber(counts[c], locale)}</span>}
            </Chip>
          ))}
        </div>
        <div className="grid gap-3 md:grid-cols-[1fr_1fr_1fr_1.4fr]">
          <TextField
            label={t('from')}
            type="date"
            value={from}
            max={to}
            onChange={(e) => e.target.value && update({ from: e.target.value })}
          />
          <TextField
            label={t('to')}
            type="date"
            value={to}
            min={from}
            onChange={(e) => e.target.value && update({ to: e.target.value })}
          />
          <SelectField
            label={t('professional')}
            value={professionalId}
            onChange={(e) => update({ professional: e.target.value || null })}
          >
            <option value="">{t('allProfessionals')}</option>
            {professionals.map((p) => (
              <option key={p.id} value={p.id}>
                {localizedName(locale, p.nameAr, p.nameEn)}
              </option>
            ))}
          </SelectField>
          <form
            role="search"
            onSubmit={(event) => {
              event.preventDefault();
              update({ q: draft.trim() || null });
            }}
          >
            <SearchField
              label={t('search')}
              placeholder={t('searchPlaceholder')}
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onClear={() => {
                setDraft('');
                update({ q: null });
              }}
            />
          </form>
        </div>
      </div>

      {list.isPending ? (
        <SkeletonList rows={5} label={t('loading')} />
      ) : list.isError ? (
        <InlineAlert tone="danger" title={t('loadError')} />
      ) : list.data.items.length === 0 ? (
        <EmptyState icon="calendar" title={t('empty')} />
      ) : (
        <>
          <table className="hidden w-full border-separate border-spacing-0 overflow-hidden rounded-card border border-border bg-surface text-start md:table">
            <caption className="sr-only">{t('caption')}</caption>
            <thead className="bg-bg-page text-helper font-bold text-text-secondary">
              <tr>
                {(['time', 'customer', 'professional', 'status', 'source'] as const).map((column) => (
                  <th key={column} scope="col" className="px-4 py-3 text-start">
                    {t(`columns.${column}`)}
                  </th>
                ))}
                <th scope="col" className="px-4 py-3">
                  <span className="sr-only">{t('columns.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {list.data.items.map((b) => (
                <tr
                  key={b.id}
                  className="border-t border-border-row text-label"
                  data-testid="appointment-row"
                >
                  <td className="border-t border-border-row px-4 py-3">
                    <span className="block font-bold text-text-primary">
                      {formatTime(b.startsAt, locale, timeZone)}
                    </span>
                    <span className="text-helper text-text-tertiary">
                      {formatDate(b.startsAt, locale, { timeZone, withWeekday: true })}
                    </span>
                  </td>
                  <td className="border-t border-border-row px-4 py-3">
                    <span className="block font-bold text-text-primary">{b.customerName}</span>
                    <span className="text-helper text-text-secondary">
                      {localizedName(locale, b.item.nameAr, b.item.nameEn)} ·{' '}
                      {formatDurationMinutes(b.item.durationMinutes, locale)}
                    </span>
                  </td>
                  <td className="border-t border-border-row px-4 py-3">
                    {localizedName(locale, b.professional.nameAr, b.professional.nameEn)}
                  </td>
                  <td className="border-t border-border-row px-4 py-3">
                    <StatusBadge kind="booking" status={b.status} size="sm" />
                  </td>
                  <td className="border-t border-border-row px-4 py-3 text-text-secondary">
                    {tDrawer(`channel.${b.channel}`)}
                  </td>
                  <td className="border-t border-border-row px-4 py-3 text-end">
                    <Button
                      size="xs"
                      variant="outline"
                      onClick={() => update({ booking: b.id }, false)}
                      aria-label={t('open', { name: b.customerName })}
                    >
                      {t('details')}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <ul className="flex flex-col gap-3 md:hidden">
            {list.data.items.map((b) => (
              <li
                key={b.id}
                className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4 shadow-e1"
                data-testid="appointment-card"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-label font-bold text-text-primary">
                    {formatTime(b.startsAt, locale, timeZone)} ·{' '}
                    {formatDate(b.startsAt, locale, { timeZone, withWeekday: false })}
                  </span>
                  <StatusBadge kind="booking" status={b.status} size="sm" />
                </div>
                <span className="text-label font-bold text-text-primary">{b.customerName}</span>
                <span className="text-helper text-text-secondary">
                  {localizedName(locale, b.item.nameAr, b.item.nameEn)} ·{' '}
                  {localizedName(locale, b.professional.nameAr, b.professional.nameEn)}
                </span>
                <Button
                  size="sm"
                  variant="outline"
                  className="self-start"
                  onClick={() => update({ booking: b.id }, false)}
                  aria-label={t('open', { name: b.customerName })}
                >
                  {t('details')}
                </Button>
              </li>
            ))}
          </ul>

          <nav aria-label={t('paging')} className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-helper text-text-secondary">
              {t('summary', {
                from: formatNumber((page - 1) * PAGE_SIZE + 1, locale),
                to: formatNumber(Math.min(page * PAGE_SIZE, total), locale),
                total: formatNumber(total, locale),
              })}
            </p>
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={page <= 1}
                onClick={() => update({ page: String(page - 1) }, false)}
              >
                {t('previous')}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={page >= pageCount}
                onClick={() => update({ page: String(page + 1) }, false)}
              >
                {t('next')}
              </Button>
            </div>
          </nav>
        </>
      )}

      <AppointmentDrawer
        bookingId={bookingId}
        timeZone={timeZone}
        canUpdate={canUpdate}
        onClose={() => update({ booking: null }, false)}
      />
    </div>
  );
}
