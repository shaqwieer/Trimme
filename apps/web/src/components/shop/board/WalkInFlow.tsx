'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { useRef, useState } from 'react';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { TextareaField, TextField } from '@/components/ui/inputs';
import { RadioCard } from '@/components/ui/selection';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { endOf } from '@/lib/booking/format';
import { keyFor, type KeyedIntent, newIdempotencyKey } from '@/lib/booking/wizard';
import { type AppLocale, formatDurationMinutes, formatPrice, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { BOARD_KEY } from './AppointmentDrawer';

export type WalkInOffer = {
  kind: 'service' | 'package';
  id: string;
  nameAr: string;
  nameEn: string | null;
  price: number;
  currency: string;
  durationMinutes: number;
};

const NOW = 'now';
const NAME_MAX = 120;
const NOTE_MAX = 500;

/**
 * Walk-in booking (s-walkin 2397–2459, D-035, D-088): service → professional (free now, busy until…, next free time) →
 * time ("start now" or one of the day's free starts, from the same rules as the command) → the customer's name and an
 * optional note, with a live summary. There is no phone field: walk-ins never collect one (spec §7). The time is
 * blocked from online booking at once.
 */
export function WalkInFlow({
  offers,
  today,
  timeZone,
  initialProfessionalId,
  initialStart,
  initialDate,
}: {
  offers: WalkInOffer[];
  today: string;
  timeZone: string;
  initialProfessionalId?: string;
  initialStart?: string;
  initialDate?: string;
}) {
  const t = useTranslations('shopBoard.walkIn');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const queryClient = useQueryClient();
  const [offerKey, setOfferKey] = useState<string>(offers[0] ? `${offers[0].kind}:${offers[0].id}` : '');
  const [date, setDate] = useState(initialDate ?? today);
  const [professionalId, setProfessionalId] = useState(initialProfessionalId ?? '');
  const [start, setStart] = useState(initialStart ?? '');
  const [name, setName] = useState('');
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const [created, setCreated] = useState<{ id: string; reference: string } | null>(null);
  const keyed = useRef<KeyedIntent | undefined>(undefined);

  const offer = offers.find((o) => `${o.kind}:${o.id}` === offerKey);
  const options = useQuery({
    queryKey: [BOARD_KEY, 'walk-in', offerKey, date],
    enabled: Boolean(offer),
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/availability/walk-in', {
          params: {
            query: {
              serviceId: offer!.kind === 'service' ? offer!.id : undefined,
              packageId: offer!.kind === 'package' ? offer!.id : undefined,
              date,
            },
          },
        }),
      ),
  });

  const professional = options.data?.professionals.find((p) => p.id === professionalId);
  const startsAt = start === NOW ? options.data?.now : start || undefined;
  const nameError =
    name.trim().length === 0 ? undefined : name.trim().length > NAME_MAX ? t('nameTooLong') : undefined;
  const ready = Boolean(offer && professional && startsAt && name.trim() && !nameError);

  if (offers.length === 0) {
    return <EmptyState icon="tag" title={t('noServices')} />;
  }

  const submit = async () => {
    if (!offer || !professional || !startsAt) return;
    setError(undefined);
    setSubmitting(true);
    const body = {
      serviceId: offer.kind === 'service' ? offer.id : null,
      packageId: offer.kind === 'package' ? offer.id : null,
      professionalId: professional.id,
      startsAt: start === NOW ? null : start,
      customerName: name.trim(),
      note: note.trim() || null,
    };
    keyed.current = keyFor(
      keyed.current,
      { offerId: offer.id, pro: professional.id, startsAt: start, note: `${name.trim()}|${note.trim()}` },
      newIdempotencyKey,
    );
    try {
      const booking = ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/walk-in', {
          params: { header: { 'Idempotency-Key': keyed.current.key } },
          body,
        }),
      );
      setCreated({ id: booking.id, reference: booking.reference });
      await queryClient.invalidateQueries({ queryKey: [BOARD_KEY] });
    } catch (failure) {
      if (failure instanceof ApiError && failure.errorCode === 'booking.slot_unavailable') {
        setError(t('taken'));
        setStart('');
        await queryClient.invalidateQueries({ queryKey: [BOARD_KEY, 'walk-in'] });
      } else {
        setError(apiMessage(failure instanceof ApiError ? failure : 'server.unexpected'));
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (created) {
    return (
      <div
        className="flex max-w-[640px] flex-col items-center gap-3 rounded-card border border-border bg-surface p-8 text-center shadow-e1"
        role="status"
      >
        <Icon name="check" className="size-10 text-success-700" />
        <h2 className="text-h3 font-bold text-navy-900">{t('done.title')}</h2>
        <p className="text-label text-text-secondary">{t('done.body', { reference: created.reference })}</p>
        <div className="flex flex-wrap justify-center gap-2">
          <ButtonLink href={`/shop/appointments?booking=${created.id}`} variant="primary" size="md">
            {t('done.view')}
          </ButtonLink>
          <Button
            variant="outline"
            size="md"
            onClick={() => {
              setCreated(null);
              setStart('');
              setName('');
              setNote('');
              keyed.current = undefined;
            }}
          >
            {t('done.another')}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="grid items-start gap-5 lg:grid-cols-[1.6fr_1fr]" data-testid="walk-in">
      <div className="flex flex-col gap-6 rounded-card border border-border bg-surface p-5 shadow-e1">
        <p className="text-helper text-text-secondary">{t('intro')}</p>

        <fieldset className="flex min-w-0 flex-col gap-2.5">
          <legend className="pb-2 text-label font-bold text-text-strong">{t('steps.service')}</legend>
          <div className="grid gap-2.5 sm:grid-cols-2">
            {offers.map((o) => (
              <RadioCard
                key={`${o.kind}:${o.id}`}
                name="walk-in-offer"
                value={`${o.kind}:${o.id}`}
                checked={offerKey === `${o.kind}:${o.id}`}
                onChange={() => {
                  setOfferKey(`${o.kind}:${o.id}`);
                  setProfessionalId('');
                  setStart('');
                }}
                aside={
                  <span className="font-latin text-label font-bold text-navy-900">
                    {formatPrice(o.price, locale, o.currency)}
                  </span>
                }
              >
                <span className="block text-label font-bold text-text-primary">
                  {localizedName(locale, o.nameAr, o.nameEn)}
                </span>
                <span className="block text-helper text-text-secondary">
                  {formatDurationMinutes(o.durationMinutes, locale)}
                </span>
              </RadioCard>
            ))}
          </div>
        </fieldset>

        <TextField
          label={t('date')}
          type="date"
          value={date}
          min={today}
          className="max-w-[220px]"
          onChange={(event) => {
            if (event.target.value) {
              setDate(event.target.value);
              setStart('');
            }
          }}
        />

        <fieldset className="flex min-w-0 flex-col gap-2.5">
          <legend className="pb-2 text-label font-bold text-text-strong">{t('steps.professional')}</legend>
          {options.isPending ? (
            <SkeletonList rows={2} label={t('loading')} />
          ) : options.isError ? (
            <InlineAlert tone="danger" title={t('loadError')} />
          ) : options.data.professionals.length === 0 ? (
            <p className="text-helper text-text-secondary">{t('noProfessionals')}</p>
          ) : (
            <div className="grid gap-2.5 sm:grid-cols-2">
              {options.data.professionals.map((p) => {
                const meta = p.freeNow
                  ? t('freeNow')
                  : p.nextFreeAt
                    ? t('freeAt', { time: formatTime(p.nextFreeAt, locale, timeZone) })
                    : t('noTimes');
                return (
                  <RadioCard
                    key={p.id}
                    name="walk-in-professional"
                    value={p.id}
                    checked={professionalId === p.id}
                    disabled={!p.freeNow && !p.nextFreeAt}
                    onChange={() => {
                      setProfessionalId(p.id);
                      setStart(p.freeNow ? NOW : '');
                    }}
                  >
                    <span className="block text-label font-bold text-text-primary">
                      {localizedName(locale, p.nameAr, p.nameEn)}
                    </span>
                    <span
                      className={`block text-helper ${p.freeNow ? 'font-bold text-success-700' : 'text-text-secondary'}`}
                    >
                      {meta}
                    </span>
                  </RadioCard>
                );
              })}
            </div>
          )}
        </fieldset>

        {professional && (
          <fieldset className="flex min-w-0 flex-col gap-2.5">
            <legend className="pb-2 text-label font-bold text-text-strong">{t('steps.time')}</legend>
            <div className="flex flex-wrap gap-2" data-testid="walk-in-times">
              {professional.freeNow && (
                <label className="relative inline-flex min-h-11 cursor-pointer items-center gap-1.5 rounded-button border-[1.5px] border-navy-900 bg-surface px-4 text-label font-bold text-navy-900 has-checked:bg-navy-900 has-checked:text-on-navy has-focus-visible:shadow-[var(--focus-ring)]">
                  <input
                    type="radio"
                    name="walk-in-time"
                    value={NOW}
                    checked={start === NOW}
                    onChange={() => setStart(NOW)}
                    className="sr-only"
                  />
                  <Icon name="play" className="size-4" />
                  {t('startNow', { time: formatTime(options.data!.now, locale, timeZone) })}
                </label>
              )}
              {professional.starts.map((s) => (
                <label
                  key={s}
                  className="relative inline-flex min-h-11 cursor-pointer items-center rounded-button border-[1.5px] border-border-input bg-surface px-3 font-latin text-label font-bold text-text-strong hover:border-brand-500 has-checked:border-navy-900 has-checked:bg-navy-900 has-checked:text-on-navy has-focus-visible:shadow-[var(--focus-ring)]"
                >
                  <input
                    type="radio"
                    name="walk-in-time"
                    value={s}
                    checked={
                      start !== NOW && start !== '' && new Date(start).getTime() === new Date(s).getTime()
                    }
                    onChange={() => setStart(s)}
                    className="sr-only"
                  />
                  {formatTime(s, locale, timeZone)}
                </label>
              ))}
            </div>
          </fieldset>
        )}

        <fieldset className="flex min-w-0 flex-col gap-3">
          <legend className="pb-2 text-label font-bold text-text-strong">{t('steps.customer')}</legend>
          <TextField
            label={t('name')}
            value={name}
            maxLength={NAME_MAX}
            autoComplete="off"
            error={nameError}
            onChange={(e) => setName(e.target.value)}
          />
          <TextareaField
            label={t('note')}
            optional
            placeholder={t('notePlaceholder')}
            maxLength={NOTE_MAX}
            rows={2}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
          <p
            className="flex items-start gap-2 rounded-card bg-bg-muted p-3 text-helper text-text-secondary"
            data-testid="walk-in-no-phone"
          >
            <Icon name="eyeOff" className="mt-0.5 size-4 shrink-0" />
            {t('noPhone')}
          </p>
        </fieldset>
      </div>

      <aside
        className="sticky bottom-0 flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e2 lg:top-24"
        aria-labelledby="walk-in-summary"
      >
        <h2 id="walk-in-summary" className="text-h3 font-bold text-navy-900">
          {t('summary.title')}
        </h2>
        <dl className="flex flex-col divide-y divide-border-row text-label">
          {[
            [t('summary.service'), offer ? localizedName(locale, offer.nameAr, offer.nameEn) : '—'],
            [
              t('summary.professional'),
              professional ? localizedName(locale, professional.nameAr, professional.nameEn) : '—',
            ],
            [t('summary.starts'), startsAt ? formatTime(startsAt, locale, timeZone) : '—'],
            [
              t('summary.ends'),
              startsAt && offer ? formatTime(endOf(startsAt, offer.durationMinutes), locale, timeZone) : '—',
            ],
            [t('summary.total'), offer ? formatPrice(offer.price, locale, offer.currency) : '—'],
          ].map(([label, value]) => (
            <div key={label} className="flex items-center justify-between gap-3 py-2">
              <dt className="text-text-secondary">{label}</dt>
              <dd className="font-bold text-text-primary">{value}</dd>
            </div>
          ))}
        </dl>
        {error && <InlineAlert tone="danger" title={error} />}
        <Button size="lg" fullWidth disabled={!ready} loading={submitting} onClick={submit}>
          {t('submit')}
        </Button>
        <p className="text-helper text-text-tertiary">{t('blocksOnline')}</p>
      </aside>
    </div>
  );
}
