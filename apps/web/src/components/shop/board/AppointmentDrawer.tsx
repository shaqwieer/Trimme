'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Timeline, type TimelineItem } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { TextareaField } from '@/components/ui/inputs';
import { Dialog, Sheet } from '@/components/ui/overlays';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatPrice,
  formatTime,
} from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { bookingSource } from '@/lib/booking/format';

type Detail = components['schemas']['ShopBookingDetailResponse'];
type Status = components['schemas']['BookingStatus'];

export const BOARD_KEY = 'shop-board';

const NOTE_MAX = 500;

const actionVariant: Partial<Record<Status, 'primary' | 'secondary' | 'outline' | 'danger'>> = {
  Confirmed: 'primary',
  Arrived: 'secondary',
  Completed: 'primary',
  NoShow: 'outline',
  CancelledByShop: 'danger',
};

/**
 * One appointment (s-appointments drawer 2344–2393): time, customer name, item, professional, source, the snapshot price
 * paid at the shop, "customer number not available" (spec §7), status actions from the API's `allowedTransitions` only
 * (DV-S08; cancelling asks for a reason), internal notes and the history. Actions are optimistic: the badge changes at
 * once and rolls back if the API refuses (a stale version, or a time rule).
 */
export function AppointmentDrawer({
  bookingId,
  timeZone,
  canUpdate,
  onClose,
}: {
  bookingId: string | null;
  timeZone: string;
  canUpdate: boolean;
  onClose: () => void;
}) {
  const t = useTranslations('shopBoard.drawer');
  const tStatus = useTranslations('status.booking');
  const locale = useLocale() as AppLocale;
  const queryClient = useQueryClient();
  const apiMessage = useApiErrorMessage();
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState<Status | null>(null);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [note, setNote] = useState('');
  const [savingNote, setSavingNote] = useState(false);
  const key = [BOARD_KEY, 'booking', bookingId];

  const detail = useQuery({
    queryKey: key,
    enabled: Boolean(bookingId),
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/bookings/{bookingId}', {
          params: { path: { bookingId: bookingId! } },
        }),
      ),
  });

  const transition = async (to: Status, why?: string) => {
    const current = queryClient.getQueryData<Detail>(key);
    if (!current || !bookingId) return;
    setError(undefined);
    setPending(to);
    // Optimistic: show the new status at once, with no further actions until the API answers.
    queryClient.setQueryData<Detail>(key, {
      ...current,
      booking: { ...current.booking, status: to, allowedTransitions: [] },
    });
    try {
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/transitions', {
          params: { path: { bookingId } },
          body: { to, reason: why ?? null, version: current.booking.version },
        }),
      );
      setCancelOpen(false);
      setReason('');
    } catch (failure) {
      queryClient.setQueryData<Detail>(key, current);
      setError(
        failure instanceof ApiError &&
          failure.status === 409 &&
          failure.errorCode !== 'booking.invalid_transition'
          ? t('stale')
          : apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'),
      );
    } finally {
      setPending(null);
      await queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
    }
  };

  const addNote = async () => {
    if (!bookingId || !note.trim()) return;
    setSavingNote(true);
    setError(undefined);
    try {
      ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/{bookingId}/notes', {
          params: { path: { bookingId } },
          body: { text: note.trim() },
        }),
      );
      setNote('');
      await queryClient.invalidateQueries({ queryKey: key });
    } catch (failure) {
      setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
    } finally {
      setSavingNote(false);
    }
  };

  const data = detail.data;
  const booking = data?.booking;
  const history: TimelineItem[] = (data?.history ?? []).map((h, index) => ({
    id: `${h.occurredAt}-${index}`,
    title:
      h.kind === 'Created'
        ? t('history.created')
        : h.kind === 'Rescheduled'
          ? t('history.rescheduled')
          : t('history.status', { status: tStatus(h.toStatus) }),
    meta: `${formatDate(h.occurredAt, locale, { timeZone })} ${formatTime(h.occurredAt, locale, timeZone)} · ${t(`actor.${h.actorType}`)}${h.reason ? ` · ${h.reason}` : ''}`,
    tone:
      h.toStatus === 'Completed'
        ? 'success'
        : h.toStatus.startsWith('Cancelled')
          ? 'danger'
          : h.toStatus === 'NoShow'
            ? 'neutral'
            : 'brand',
  }));

  return (
    <Sheet
      open={Boolean(bookingId)}
      onOpenChange={(open) => {
        if (!open) {
          setError(undefined);
          onClose();
        }
      }}
      side="end"
      title={t('title')}
      closeLabel={t('close')}
    >
      <div className="flex flex-col gap-4 px-5 pt-2 pb-6" data-testid="appointment-drawer">
        {detail.isPending ? (
          <SkeletonList rows={4} label={t('loading')} />
        ) : !booking ? (
          <InlineAlert tone="danger" title={t('notFound')} />
        ) : (
          <>
            <p className="font-latin text-helper font-bold text-text-tertiary" dir="ltr">
              {booking.reference}
            </p>
            <div className="flex flex-col gap-1 rounded-card bg-bg-subtle p-4">
              <span className="text-helper text-text-strong">{t('when')}</span>
              <span className="text-[1.375rem] font-extrabold text-navy-900">
                {formatTime(booking.startsAt, locale, timeZone)} —{' '}
                {formatTime(booking.endsAt, locale, timeZone)}
              </span>
              <span className="text-helper text-text-strong">
                {formatDate(booking.startsAt, locale, { timeZone })} ·{' '}
                {formatDurationMinutes(booking.item.durationMinutes, locale)}
              </span>
              <StatusBadge kind="booking" status={booking.status} className="mt-1 self-start" />
            </div>
            {booking.outsideSchedule && <InlineAlert tone="warning" title={t('outsideSchedule')} />}
            <dl className="flex flex-col divide-y divide-border-row">
              {[
                [t('rows.customer'), booking.customerName],
                [t('rows.service'), localizedName(locale, booking.item.nameAr, booking.item.nameEn)],
                [
                  t('rows.professional'),
                  localizedName(locale, booking.professional.nameAr, booking.professional.nameEn),
                ],
                [t('rows.source'), t(`channel.${bookingSource(booking)}`)],
                [
                  t('rows.amount'),
                  t('amount', { price: formatPrice(booking.item.price, locale, booking.item.currency) }),
                ],
                ...(booking.note ? [[t('rows.note'), booking.note]] : []),
                ...(booking.cancellationReason
                  ? [[t('rows.cancellationReason'), booking.cancellationReason]]
                  : []),
              ].map(([label, value]) => (
                <div key={label} className="flex items-start justify-between gap-3 py-2.5">
                  <dt className="text-helper text-text-secondary">{label}</dt>
                  <dd className="text-end text-label font-bold text-text-primary">{value}</dd>
                </div>
              ))}
            </dl>
            <p
              className="flex items-start gap-2 rounded-card bg-bg-muted p-3 text-helper text-text-secondary"
              data-testid="no-phone-note"
            >
              <Icon name="eyeOff" className="mt-0.5 size-4 shrink-0" />
              <span>
                <strong className="block text-text-strong">{t('noPhone.title')}</strong>
                {t('noPhone.body')}
              </span>
            </p>

            {error && <InlineAlert tone="danger" title={error} />}

            {canUpdate && booking.allowedTransitions.length > 0 && (
              <section aria-labelledby="status-actions" className="flex flex-col gap-2">
                <h3 id="status-actions" className="text-label font-bold text-text-primary">
                  {t('changeStatus')}
                </h3>
                <div className="flex flex-wrap gap-2">
                  {booking.allowedTransitions.map((to) => (
                    <Button
                      key={to}
                      size="sm"
                      variant={actionVariant[to] ?? 'outline'}
                      loading={pending === to}
                      disabled={pending !== null}
                      onClick={() => (to === 'CancelledByShop' ? setCancelOpen(true) : void transition(to))}
                    >
                      {t(`actions.${to}`)}
                    </Button>
                  ))}
                </div>
              </section>
            )}

            {canUpdate && (
              <section aria-labelledby="notes-title" className="flex flex-col gap-2">
                <h3 id="notes-title" className="text-label font-bold text-text-primary">
                  {t('notes.title')}
                </h3>
                {data!.notes.length > 0 && (
                  <ul className="flex flex-col gap-2">
                    {data!.notes.map((n) => (
                      <li key={n.id} className="rounded-card bg-bg-subtle p-3 text-label text-text-strong">
                        {n.text}
                        <span className="block text-helper text-text-tertiary">
                          {formatDate(n.createdAt, locale, { timeZone })}{' '}
                          {formatTime(n.createdAt, locale, timeZone)}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
                <TextareaField
                  label={t('notes.add')}
                  helper={t('notes.internal')}
                  maxLength={NOTE_MAX}
                  rows={2}
                  value={note}
                  onChange={(event) => setNote(event.target.value)}
                />
                <Button
                  size="sm"
                  variant="secondary"
                  className="self-start"
                  loading={savingNote}
                  disabled={!note.trim()}
                  onClick={addNote}
                >
                  {t('notes.save')}
                </Button>
              </section>
            )}

            <section aria-labelledby="history-title" className="flex flex-col gap-2">
              <h3 id="history-title" className="text-label font-bold text-text-primary">
                {t('history.title')}
              </h3>
              <Timeline items={history} />
            </section>
          </>
        )}
      </div>

      <Dialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        size="sm"
        title={t('cancel.title')}
        description={t('cancel.body')}
        footer={
          <>
            <Button
              variant="dangerSolid"
              className="flex-1"
              disabled={!reason.trim()}
              loading={pending === 'CancelledByShop'}
              onClick={() => void transition('CancelledByShop', reason.trim())}
            >
              {t('cancel.confirm')}
            </Button>
            <Button variant="outline" className="flex-1" onClick={() => setCancelOpen(false)}>
              {t('cancel.keep')}
            </Button>
          </>
        }
      >
        <TextareaField
          label={t('cancel.reason')}
          maxLength={NOTE_MAX}
          rows={2}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
        />
      </Dialog>
    </Sheet>
  );
}
