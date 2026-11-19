'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useState } from 'react';
import { useDisplayCalendar } from '@/components/providers/CalendarProvider';
import { Badge, StatusBadge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Timeline, type TimelineItem } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { TextareaField } from '@/components/ui/inputs';
import { RadioCard } from '@/components/ui/selection';
import { ConfirmDialog, Dialog, Sheet } from '@/components/ui/overlays';
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
import { BOARD_KEY } from './boardKey';
import { MoveDialog, RescheduleDialog } from './BookingChangeDialogs';
import { BookingServicesEditor } from './BookingServicesEditor';
import { CustomerVisitBadge } from './CustomerVisitBadge';

export { BOARD_KEY };

type Detail = components['schemas']['ShopBookingDetailResponse'];
type Status = components['schemas']['BookingStatus'];
type ReasonCode = NonNullable<components['schemas']['ShopCancellationReason']>;

/** The app's ready reasons (D-151, the client's 2-7), so the reports can count them; «سبب ثاني» is typed. */
const REASONS: ReasonCode[] = ['StaffUnwell', 'Emergency', 'ShopClosed', 'Other'];

const NOTE_MAX = 500;

const actionVariant: Partial<Record<Status, 'primary' | 'secondary' | 'outline' | 'danger'>> = {
  Confirmed: 'primary',
  Arrived: 'primary',
  Completed: 'primary',
  NoShow: 'outline',
  CancelledByShop: 'danger',
};

/**
 * One appointment (s-appointments drawer 2344–2393): time, customer name, item, professional, source, the snapshot price
 * paid at the shop, status actions from the API's `allowedTransitions` only (DV-S08), internal notes and the history.
 * D-147: the owner only watches (no actions, notes read-only); a barber's visit goes «وصل» then «انتهت الخدمة», with
 * «الزبون ما حضر» a quarter hour late; emergencies come in the client's order — another barber, a new time, then a
 * cancellation with a reason (in the last quarter hour it is «إلغاء متأخر»; once arrived, never). The customer's mobile
 * shows for a walk-in or phone booking while it is open, to barbers only (the API decides); otherwise «رقم العميل غير
 * متاح». The log names who did each thing. On a colleague's booking a barber may only accept or reject it while pending,
 * or close it once red.
 */
export function AppointmentDrawer({
  bookingId,
  timeZone,
  canUpdate,
  canReschedule = false,
  canMove = false,
  ownProfessionalId = null,
  onClose,
}: {
  bookingId: string | null;
  timeZone: string;
  canUpdate: boolean;
  /** `Shop.Bookings.Reschedule`: a new time with the same barber. */
  canReschedule?: boolean;
  /** `Shop.Bookings.ChangeProfessional`: the same time with another barber. */
  canMove?: boolean;
  /** A barber's own professional (D-145); null for the owner. */
  ownProfessionalId?: string | null;
  onClose: () => void;
}) {
  const t = useTranslations('shopBoard.drawer');
  const tStatus = useTranslations('status.booking');
  const tLoyalty = useTranslations('loyalty');
  const tReasons = useTranslations('shopBoard.drawer.reasons');
  const locale = useLocale() as AppLocale;
  const calendar = useDisplayCalendar();
  const queryClient = useQueryClient();
  const apiMessage = useApiErrorMessage();
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState<Status | null>(null);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState<'Completed' | 'NoShow' | null>(null);
  const [reason, setReason] = useState('');
  const [reasonCode, setReasonCode] = useState<ReasonCode | null>(null);
  const [note, setNote] = useState('');
  const [savingNote, setSavingNote] = useState(false);
  const [changing, setChanging] = useState<'reschedule' | 'move' | null>(null);
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

  const transition = async (to: Status, why?: string, code?: ReasonCode | null) => {
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
          body: {
            to,
            reason: why ?? null,
            ...(code ? { reasonCode: code } : {}),
            version: current.booking.version,
          },
        }),
      );
      setCancelOpen(false);
      setConfirmOpen(null);
      setReason('');
      setReasonCode(null);
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
          : h.kind === 'ProfessionalChanged'
            ? t('history.professionalChanged', {
                previous: h.previousProfessional
                  ? localizedName(locale, h.previousProfessional.nameAr, h.previousProfessional.nameEn)
                  : '',
              })
            : h.kind === 'ServiceAdded'
              ? t('history.serviceAdded', { name: h.reason ?? '' })
              : h.kind === 'ServiceRemoved'
                ? t('history.serviceRemoved', { name: h.reason ?? '' })
                : h.kind === 'PostponementOffered'
                  ? t('history.postponementOffered')
                  : h.kind === 'PostponementAccepted'
                    ? t('history.postponementAccepted')
                    : h.kind === 'PostponementDeclined'
                      ? t('history.postponementDeclined')
                      : t('history.status', { status: tStatus(h.toStatus) }),
    meta: `${formatDate(h.occurredAt, locale, { timeZone, calendar })} ${formatTime(h.occurredAt, locale, timeZone)} — ${h.actorName ?? t(`actor.${h.actorType}`)}${h.reason && h.kind !== 'ServiceAdded' && h.kind !== 'ServiceRemoved' ? ` — ${h.reason}` : ''}`,
    tone:
      h.toStatus === 'Completed'
        ? 'success'
        : h.toStatus.startsWith('Cancelled')
          ? 'danger'
          : h.toStatus === 'NoShow'
            ? 'neutral'
            : 'brand',
  }));

  // A barber's own booking (the owner, with no professional, has no actions at all).
  const mine = booking !== undefined && (!ownProfessionalId || booking.professional.id === ownProfessionalId);
  const allowed = canUpdate && booking ? booking.allowedTransitions : [];
  // «رفض» for a pending booking sits with the status actions; any other cancellation is the last emergency step.
  const rejects = booking?.status === 'Pending';
  const statusActions = allowed.filter((to) => to !== 'CancelledByShop' || rejects);
  const canCancel = allowed.includes('CancelledByShop') && !rejects;
  // Pending or Confirmed, and not started when it was read: the API's rule for both changes (it has the last word).
  const changeable =
    booking !== undefined &&
    mine &&
    (booking.status === 'Pending' || booking.status === 'Confirmed') &&
    Date.parse(booking.startsAt) > detail.dataUpdatedAt;
  const showEmergencies = (changeable && (canReschedule || canMove)) || canCancel;
  const target = booking
    ? {
        id: booking.id,
        version: booking.version,
        startsAt: booking.startsAt,
        customerName: booking.customerName,
      }
    : null;
  const changed = () => queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
  const editableServices =
    canUpdate &&
    mine &&
    booking !== undefined &&
    ['Pending', 'Confirmed', 'Arrived'].includes(booking.status);

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
                {formatDate(booking.startsAt, locale, { timeZone, calendar })} —{' '}
                {formatDurationMinutes(booking.item.durationMinutes, locale)}
              </span>
              <span className="mt-1 flex flex-wrap gap-1.5">
                <StatusBadge kind="booking" status={booking.status} />
                {booking.overdue && <Badge tone="danger">{t('flags.overdue')}</Badge>}
                {booking.closedLate && <Badge tone="warning">{t('flags.closedLate')}</Badge>}
                {booking.cancelledLate && <Badge tone="danger">{t('flags.cancelledLate')}</Badge>}
              </span>
            </div>
            {/* D-151, the client's 1-2: what happened, then who may close it. */}
            {booking.overdue && (
              <div data-testid="drawer-overdue">
                <InlineAlert
                  tone="danger"
                  title={booking.status === 'Arrived' ? t('overdue.arrivedTitle') : t('overdue.title')}
                >
                  <p>{t('overdue.body')}</p>
                </InlineAlert>
              </div>
            )}
            {booking.closedBy && (
              <p className="text-label font-bold text-text-strong" data-testid="drawer-closed-by">
                {t('closedLine', {
                  responsible: localizedName(
                    locale,
                    booking.professional.nameAr,
                    booking.professional.nameEn,
                  ),
                  closer: booking.closedBy,
                })}
              </p>
            )}
            {booking.postponement &&
              (booking.postponement.live ? (
                <InlineAlert
                  tone="info"
                  title={t('postponement.waiting', {
                    when: `${formatDate(booking.postponement.startsAt, locale, { timeZone, calendar })} ${formatTime(booking.postponement.startsAt, locale, timeZone)}`,
                    until: formatTime(booking.postponement.expiresAt, locale, timeZone),
                  })}
                />
              ) : booking.postponement.status === 'Declined' ? (
                <InlineAlert tone="warning" title={t('postponement.declined')} />
              ) : booking.postponement.status === 'Open' ? (
                <InlineAlert tone="warning" title={t('postponement.lapsed')} />
              ) : null)}
            {booking.outsideSchedule && <InlineAlert tone="warning" title={t('outsideSchedule')} />}
            {booking.loyaltyReward && (
              <InlineAlert
                tone="success"
                title={tLoyalty('applied', {
                  reward: localizedName(locale, booking.loyaltyReward.titleAr, booking.loyaltyReward.titleEn),
                })}
              >
                {/* A points reward (D-149) is already taken off what the customer pays. */}
                {booking.loyaltyReward.discount != null && booking.amountDue != null && (
                  <p data-testid="drawer-amount-due">
                    {tLoyalty('due', {
                      amount: formatPrice(booking.amountDue, locale, booking.item.currency),
                    })}
                  </p>
                )}
              </InlineAlert>
            )}
            <dl className="flex flex-col divide-y divide-border-row">
              {(
                [
                  [
                    t('rows.customer'),
                    <span key="customer" className="inline-flex flex-wrap items-center justify-end gap-2">
                      {booking.customerName}
                      <CustomerVisitBadge visit={booking.visit} />
                    </span>,
                  ],
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
                  ...(booking.actualStartAt
                    ? [[t('rows.actualStart'), formatTime(booking.actualStartAt, locale, timeZone)]]
                    : []),
                  ...(booking.actualEndAt
                    ? [[t('rows.actualEnd'), formatTime(booking.actualEndAt, locale, timeZone)]]
                    : []),
                  ...(booking.closedBy ? [[t('rows.closedBy'), booking.closedBy]] : []),
                  ...(booking.note ? [[t('rows.note'), booking.note]] : []),
                  ...(booking.cancellationReason || booking.cancellationReasonCode
                    ? [
                        [
                          t('rows.cancellationReason'),
                          [
                            booking.cancellationReasonCode && booking.cancellationReasonCode !== 'Other'
                              ? tReasons(booking.cancellationReasonCode)
                              : null,
                            booking.cancellationReason,
                          ]
                            .filter(Boolean)
                            .join(' — '),
                        ],
                      ]
                    : []),
                ] as Array<[string, ReactNode]>
              ).map(([label, value]) => (
                <div key={label} className="flex items-start justify-between gap-3 py-2.5">
                  <dt className="text-helper text-text-secondary">{label}</dt>
                  <dd className="text-end text-label font-bold text-text-primary">{value}</dd>
                </div>
              ))}
            </dl>
            {booking.customerPhone ? (
              <p
                className="flex items-start gap-2 rounded-card bg-bg-subtle p-3 text-helper text-text-secondary"
                data-testid="customer-phone"
              >
                <Icon name="phone" className="mt-0.5 size-4 shrink-0" />
                <span>
                  <strong className="block text-text-strong">{t('phone.title')}</strong>
                  <a
                    href={`tel:${booking.customerPhone}`}
                    dir="ltr"
                    className="font-latin text-label font-bold text-brand-700 hover:underline"
                  >
                    {booking.customerPhone}
                  </a>
                  <span className="block">{t('phone.body')}</span>
                </span>
              </p>
            ) : (
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
            )}

            {error && <InlineAlert tone="danger" title={error} />}

            {statusActions.length > 0 && (
              <section aria-labelledby="status-actions" className="flex flex-col gap-2">
                <h3 id="status-actions" className="text-label font-bold text-text-primary">
                  {t('changeStatus')}
                </h3>
                <div className="flex flex-wrap gap-2">
                  {statusActions.map((to) => (
                    <Button
                      key={to}
                      size="sm"
                      variant={actionVariant[to] ?? 'outline'}
                      loading={pending === to}
                      disabled={pending !== null}
                      onClick={() =>
                        to === 'CancelledByShop'
                          ? setCancelOpen(true)
                          : to === 'Completed' || to === 'NoShow'
                            ? setConfirmOpen(to)
                            : void transition(to)
                      }
                    >
                      {to === 'CancelledByShop' ? t('actions.reject') : t(`actions.${to}`)}
                    </Button>
                  ))}
                </div>
              </section>
            )}

            {editableServices && (
              <BookingServicesEditor
                booking={booking}
                onChanged={() => queryClient.invalidateQueries({ queryKey: [BOARD_KEY] })}
              />
            )}

            {showEmergencies && (
              <section aria-labelledby="change-actions" className="flex flex-col gap-2">
                <h3 id="change-actions" className="text-label font-bold text-text-primary">
                  {t('change.title')}
                </h3>
                <p className="text-helper text-text-secondary">{t('change.order')}</p>
                <div className="flex flex-wrap gap-2">
                  {changeable && canMove && (
                    <Button size="sm" variant="outline" onClick={() => setChanging('move')}>
                      {t('change.move')}
                    </Button>
                  )}
                  {changeable && canReschedule && (
                    <Button size="sm" variant="outline" onClick={() => setChanging('reschedule')}>
                      {t('change.reschedule')}
                    </Button>
                  )}
                  {canCancel && (
                    <Button size="sm" variant="danger" onClick={() => setCancelOpen(true)}>
                      {t('actions.CancelledByShop')}
                    </Button>
                  )}
                </div>
              </section>
            )}

            {(data!.notes.length > 0 || (canUpdate && mine)) && (
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
                          {formatDate(n.createdAt, locale, { timeZone, calendar })}{' '}
                          {formatTime(n.createdAt, locale, timeZone)}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
                {canUpdate && mine && (
                  <>
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
                  </>
                )}
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

      {target && changing === 'reschedule' && (
        <RescheduleDialog
          booking={target}
          timeZone={timeZone}
          offer={booking?.channel === 'Online'}
          onClose={() => setChanging(null)}
          onChanged={changed}
        />
      )}
      {target && changing === 'move' && (
        <MoveDialog
          booking={target}
          timeZone={timeZone}
          onClose={() => setChanging(null)}
          onChanged={changed}
        />
      )}

      {booking && (
        <ConfirmDialog
          open={confirmOpen !== null}
          onOpenChange={(open) => !open && setConfirmOpen(null)}
          tone={confirmOpen === 'NoShow' ? 'danger' : 'default'}
          title={
            confirmOpen === 'NoShow'
              ? t('noShow.title', { name: booking.customerName })
              : t('finish.title', { name: booking.customerName })
          }
          body={confirmOpen === 'NoShow' ? t('noShow.body') : t('finish.body')}
          confirmLabel={confirmOpen === 'NoShow' ? t('noShow.confirm') : t('finish.confirm')}
          cancelLabel={t('finish.keep')}
          loading={pending === 'Completed' || pending === 'NoShow'}
          onConfirm={() => confirmOpen && void transition(confirmOpen)}
        />
      )}

      <Dialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        size="sm"
        title={rejects ? t('reject.title') : t('cancel.title')}
        description={rejects ? t('reject.body') : t('cancel.body')}
        footer={
          <>
            <Button
              variant="dangerSolid"
              className="flex-1"
              disabled={rejects || reasonCode === 'Other' ? reason.trim().length < 3 : reasonCode === null}
              loading={pending === 'CancelledByShop'}
              onClick={() =>
                void transition(
                  'CancelledByShop',
                  // The text goes with a rejection or «سبب ثاني» only; a ready reason is its code.
                  rejects || reasonCode === 'Other' ? reason.trim() : undefined,
                  rejects ? null : reasonCode,
                )
              }
            >
              {rejects ? t('reject.confirm') : t('cancel.confirm')}
            </Button>
            <Button variant="outline" className="flex-1" onClick={() => setCancelOpen(false)}>
              {t('cancel.keep')}
            </Button>
          </>
        }
      >
        {rejects ? (
          <TextareaField
            label={t('cancel.reason')}
            maxLength={NOTE_MAX}
            rows={2}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        ) : (
          <div className="flex flex-col gap-3">
            <fieldset className="flex min-w-0 flex-col gap-2" data-testid="cancel-reasons">
              <legend className="mb-1 text-label font-bold text-text-strong">{t('cancel.reason')}</legend>
              <div className="grid gap-2 sm:grid-cols-2">
                {REASONS.map((code) => (
                  <RadioCard
                    key={code}
                    name="cancellationReason"
                    value={code}
                    checked={reasonCode === code}
                    onChange={() => setReasonCode(code)}
                  >
                    {tReasons(code)}
                  </RadioCard>
                ))}
              </div>
            </fieldset>
            {reasonCode === 'Other' && (
              <TextareaField
                label={t('cancel.otherReason')}
                maxLength={NOTE_MAX}
                rows={2}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              />
            )}
            <p className="text-helper text-text-secondary">{t('cancel.late')}</p>
          </div>
        )}
      </Dialog>
    </Sheet>
  );
}
