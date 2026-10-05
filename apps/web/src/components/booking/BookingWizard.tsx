'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useEffect, useRef, useState } from 'react';
import { Avatar } from '@/components/ui/Avatar';
import { Button, IconButton } from '@/components/ui/Button';
import { BookingProgress, DateStrip, HourMinutePicker } from '@/components/ui/booking';
import { ProfessionalOption, ServiceTile } from '@/components/ui/cards';
import { Icon, type DesignIconName } from '@/components/ui/icons';
import { TextareaField } from '@/components/ui/inputs';
import { RadioCard } from '@/components/ui/selection';
import { InlineAlert, SkeletonList } from '@/components/ui/states';
import { Link, useRouter } from '@/i18n/navigation';
import { browserApi, refreshSession } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import { withReturnTo } from '@/lib/auth/paths';
import {
  ANY_PROFESSIONAL,
  currentStep,
  eligibleProfessionals,
  keyFor,
  type KeyedIntent,
  MAX_SERVICES,
  newIdempotencyKey,
  nextStep,
  offerKey,
  previousStep,
  readWizardQuery,
  resolveStart,
  restoreSelection,
  selectionFrom,
  WIZARD_STEPS,
  type WizardNotice,
  wizardNotice,
  type WizardOffer,
  type WizardSelection,
  type WizardStep,
  wizardPath,
  withItems,
} from '@/lib/booking/wizard';
import { cutoffParts, endOf } from '@/lib/booking/format';
import { type AppLocale, formatDurationMinutes, formatPrice, formatTime } from '@/lib/i18n/format';
import { formatLocalDate, todayLocal } from '@/lib/i18n/localDate';
import { langIfOther, localizedName, localizedText } from '@/lib/i18n/localized';

export type WizardShop = {
  slug: string;
  nameAr: string;
  nameEn: string;
  area: string | null;
  timeZone: string;
  cancellationCutoffMinutes: number;
  logoUrl: string | null;
};

export type WizardPro = {
  id: string;
  nameAr: string;
  nameEn: string;
  specialtyAr: string | null;
  specialtyEn: string | null;
  avatarUrl: string | null;
  rating: number;
  reviewCount: number;
};

/** Who is looking: a guest confirms after signing in (D-096); staff accounts cannot book. */
export type WizardViewer = 'guest' | 'customer' | 'staff';

const NOTE_MAX = 500;

/** The customer's view of the steps (D-125): the shop is already chosen; service and professional, then date and time. */
const PHASES = ['shop', 'service', 'time', 'confirm'] as const;
const PHASE_OF: Record<WizardStep, number> = { service: 1, professional: 1, date: 2, review: 3 };

function offerQuery(items: WizardOffer[], pro: string) {
  const services = items.filter((item) => item.kind === 'service').map((item) => item.id);
  return {
    serviceId: services.length === 1 ? services[0] : undefined,
    serviceIds: services.length > 1 ? services : undefined,
    packageId: items.find((item) => item.kind === 'package')?.id,
    professionalId: pro === ANY_PROFESSIONAL ? undefined : pro,
  };
}

/**
 * The tab's last choices per shop, so going back to the shop page and into the wizard again keeps them (D-125). It is
 * memory only (no web storage, spec §9): it lasts across in-app navigation, not a reload.
 */
const lastChoices = new Map<string, string>();

function remember(slug: string, selection: WizardSelection) {
  if (selection.items.length === 0) lastChoices.delete(slug);
  else lastChoices.set(slug, wizardPath(slug, selection, 'service').split('?')[1] ?? '');
}

function recall(slug: string): string | undefined {
  return lastChoices.get(slug);
}

/**
 * The booking wizard (c-booking, spec §12, D-028, D-096, D-125): one or more services or a package → professional or
 * "any" → date (with the nearest free time offered first) → hour, then minutes → review → confirmation. Only bookable
 * dates and slots are offered (server-computed, D-009). Going back never drops a choice. Guests review everything and
 * sign in when they confirm; the wizard URL (with the review step) is the `returnTo`, so they come back to the same
 * choice, which is checked again. The submit carries an idempotency key, and a time taken meanwhile sends the customer
 * back to fresh slots.
 */
export function BookingWizard({
  shop,
  offers,
  professionals,
  viewer,
}: {
  shop: WizardShop;
  offers: WizardOffer[];
  professionals: WizardPro[];
  viewer: WizardViewer;
}) {
  const t = useTranslations('booking');
  const locale = useLocale() as AppLocale;
  const router = useRouter();
  const queryClient = useQueryClient();
  const apiMessage = useApiErrorMessage();
  const searchParams = useSearchParams();

  const query = readWizardQuery(searchParams);
  const selection = selectionFrom(query, offers, professionals);
  const step = currentStep(query, selection);
  const stepIndex = WIZARD_STEPS.indexOf(step);
  const { items, offer, pro, date, time } = selection;
  const itemsKey = offerKey(items);
  const today = todayLocal(shop.timeZone);

  const notice = wizardNotice(query);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const keyed = useRef<KeyedIntent | undefined>(undefined);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const latest = useRef<WizardSelection | null>(null);

  const go = (
    next: WizardSelection,
    to: WizardStep,
    mode: 'push' | 'replace' = 'push',
    reason?: WizardNotice,
  ) => {
    latest.current = next;
    remember(shop.slug, next);
    const url = `/${locale}${wizardPath(shop.slug, next, to, reason)}`;
    window.history[mode === 'push' ? 'pushState' : 'replaceState'](null, '', url);
  };

  // Going back must never drop a choice (D-125). A bare link to the wizard picks up this tab's last choices at the shop;
  // a history entry from before the later steps were chosen (the browser's or phone's back button) gets them back.
  useEffect(() => {
    const previous = latest.current;
    if (!previous) {
      latest.current = selection;
      const stored = recall(shop.slug);
      const bare = !query.package && query.services.length === 0 && !query.pro && !query.date;
      if (bare && stored) {
        const restored = selectionFrom(readWizardQuery(new URLSearchParams(stored)), offers, professionals);
        if (restored.items.length > 0) go(restored, 'service', 'replace');
      } else if (selection.items.length > 0) {
        remember(shop.slug, selection);
      }
      return;
    }
    const restored = restoreSelection(selection, previous);
    if (wizardPath(shop.slug, restored, step) !== wizardPath(shop.slug, selection, step)) {
      go(restored, step, 'replace');
    } else {
      latest.current = selection;
    }
    // Runs when the URL changes; `selection`, `step` and `go` are derived from it on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  // Move focus to the step heading when the step changes, so keyboard and screen-reader users follow along.
  const shownStep = useRef(step);
  useEffect(() => {
    if (shownStep.current !== step) {
      shownStep.current = step;
      headingRef.current?.focus();
    }
  }, [step]);

  const dates = useQuery({
    queryKey: ['booking-dates', shop.slug, itemsKey, pro],
    enabled: Boolean(offer) && stepIndex >= 2,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/public/shops/{slug}/availability/dates', {
          params: { path: { slug: shop.slug }, query: offerQuery(items, pro) },
        }),
      ),
  });

  const slotsOf = (day: string | undefined, enabled: boolean) => ({
    queryKey: ['booking-slots', shop.slug, itemsKey, pro, day],
    enabled: Boolean(offer && day) && enabled,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/public/shops/{slug}/availability/slots', {
          params: { path: { slug: shop.slug }, query: { ...offerQuery(items, pro), date: day! } },
        }),
      ),
  });

  // The chosen day's times, shown under the days at once (D-129) and checked again on the review.
  const slots = useQuery(slotsOf(date, stepIndex >= 2));
  const timesRef = useRef<HTMLDivElement>(null);
  const scrollToTimes = useRef(false);

  // The nearest free time, offered before the customer looks through days and hours (D-125): one extra request, for
  // the first day that has a free slot (the same cache entry the time step reads when that day is picked).
  const nearestDate = dates.data?.dates.find((d) => d.slotCount > 0 && d.date >= today)?.date;
  const nearest = useQuery(slotsOf(nearestDate, step === 'date'));
  const nearestSlot = nearest.data?.bookable === false ? undefined : nearest.data?.slots[0];

  const startsAt = resolveStart(slots.data?.slots, time);
  const blocked = dates.data?.bookable === false || slots.data?.bookable === false;
  const blockedReason = dates.data?.blockedReason ?? slots.data?.blockedReason;
  const dateOk = Boolean(date && dates.data?.dates.some((d) => d.date === date && d.slotCount > 0));

  // A date or time from the URL (a shared link, the sign-in round trip, a change of services) is checked against fresh
  // availability.
  useEffect(() => {
    if (blocked) return;
    if (stepIndex >= 3 && date && dates.data) {
      const day = dates.data.dates.find((d) => d.date === date);
      if (!day || day.slotCount === 0) {
        go({ ...selection, date: undefined, time: undefined }, 'date', 'replace', 'dateGone');
        return;
      }
    }
    if (step === 'review' && time && slots.data && !startsAt) {
      go({ ...selection, time: undefined }, 'date', 'replace', 'timeGone');
    }
    // `go` and `selection` are derived from the URL on every render; the data answers are what trigger the checks.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dates.data, slots.data, step, date, time, startsAt, blocked]);

  useEffect(() => {
    if (scrollToTimes.current && dateOk) {
      scrollToTimes.current = false;
      timesRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [date, dateOk]);

  const eligible = eligibleProfessionals(offer, professionals);
  const noCommonPro = items.length > 1 && eligible.length === 0;
  const chosenPro = professionals.find((p) => p.id === pro);
  const offerName = offer ? localizedName(locale, offer.nameAr, offer.nameEn) : '';
  const proName = chosenPro ? localizedName(locale, chosenPro.nameAr, chosenPro.nameEn) : t('any.title');
  const shopName = localizedName(locale, shop.nameAr, shop.nameEn);
  const cutoff = cutoffParts(shop.cancellationCutoffMinutes);
  const dayLabel = (day: string) =>
    day === today
      ? t('today')
      : formatLocalDate(day, locale, { weekday: 'long', day: 'numeric', month: 'long' });
  const dateLabel = date ? dayLabel(date) : '';
  const timeLabel = startsAt ? formatTime(startsAt, locale, shop.timeZone) : '';
  const itemsLabel =
    items.length > 1
      ? `${t('selectedCount', { count: items.length })} · ${formatDurationMinutes(offer!.durationMinutes, locale)}`
      : offerName;

  const canContinue =
    !blocked &&
    ((step === 'service' && Boolean(offer) && !noCommonPro) ||
      step === 'professional' ||
      (step === 'date' && dateOk && Boolean(startsAt)));

  const goNext = () => {
    const to = nextStep(step);
    if (to) go(selection, to);
  };

  const goBack = () => {
    const to = previousStep(step);
    if (to) {
      setError(undefined);
      go(selection, to);
    }
  };

  const toggle = (item: WizardOffer) => {
    const chosen = items.some((i) => i.kind === item.kind && i.id === item.id);
    // A package is booked on its own; services are booked together (up to the API's limit).
    const next =
      item.kind === 'package'
        ? chosen
          ? []
          : [item]
        : chosen
          ? items.filter((i) => !(i.kind === item.kind && i.id === item.id))
          : [...items.filter((i) => i.kind === 'service'), item].slice(0, MAX_SERVICES);
    go(withItems(selection, next, professionals), 'service', 'replace');
  };

  const confirm = async () => {
    if (!offer || !startsAt) return;
    setError(undefined);
    const returnTo = wizardPath(shop.slug, selection, 'review');
    // A guest may still hold a refresh cookie (the short-lived access cookie expired): try that before signing in.
    if (viewer === 'guest' && !(await refreshSession())) {
      router.push(withReturnTo('/auth/sign-in', returnTo));
      return;
    }

    const trimmed = note.trim();
    keyed.current = keyFor(
      keyed.current,
      { offerId: itemsKey, pro, startsAt, note: trimmed },
      newIdempotencyKey,
    );
    const target = offerQuery(items, pro);
    setSubmitting(true);
    try {
      const booking = ensureOk(
        await browserApi.POST('/api/v1/bookings', {
          params: { header: { 'Idempotency-Key': keyed.current.key } },
          body: {
            shopSlug: shop.slug,
            serviceId: target.serviceId ?? null,
            serviceIds: target.serviceIds ?? null,
            packageId: target.packageId ?? null,
            professionalId: target.professionalId ?? null,
            startsAt,
            note: trimmed || null,
          },
        }),
      );
      remember(shop.slug, { items: [], pro: ANY_PROFESSIONAL });
      router.push(`/account/bookings/${booking.id}?created=1`);
    } catch (failure) {
      setSubmitting(false);
      if (!(failure instanceof ApiError)) {
        setError(apiMessage('server.unexpected'));
        return;
      }
      if (failure.status === 401) {
        router.push(withReturnTo('/auth/sign-in', returnTo));
      } else if (failure.errorCode === 'booking.profile_incomplete') {
        router.push(withReturnTo('/auth/complete-profile', returnTo));
      } else if (failure.errorCode === 'booking.slot_unavailable') {
        await queryClient.invalidateQueries({ queryKey: ['booking-slots'] });
        await queryClient.invalidateQueries({ queryKey: ['booking-dates'] });
        go({ ...selection, time: undefined }, 'date', 'replace', 'conflict');
      } else if (failure.status === 403) {
        setError(t('customersOnly'));
      } else {
        setError(apiMessage(failure));
      }
    }
  };

  const footerNote =
    step === 'service'
      ? itemsLabel
      : step === 'professional' || (step === 'date' && !dateOk)
        ? [itemsLabel, proName].filter(Boolean).join(' · ')
        : [dateLabel, timeLabel].filter(Boolean).join(' · ');

  return (
    <div className="mx-auto flex w-full max-w-[720px] flex-1 flex-col">
      <header className="flex flex-col gap-4 px-4 pt-4 md:px-6">
        <div className="flex items-center gap-3">
          {step === 'service' ? (
            <Link
              href={`/shops/${shop.slug}`}
              aria-label={t('backToShop')}
              className="inline-flex size-11 items-center justify-center rounded-button border border-border bg-surface text-text-strong hover:bg-bg-subtle"
            >
              <Icon name="chevL" className="size-5" />
            </Link>
          ) : (
            <IconButton icon="chevL" label={t('back')} variant="outline" onClick={goBack} />
          )}
          <div className="min-w-0 flex-1">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="text-[1.25rem] font-bold text-navy-900 outline-none"
              data-testid="wizard-title"
            >
              {t(`titles.${step}`)}
            </h1>
            <p className="truncate text-helper text-text-secondary">
              {[shopName, shop.area].filter(Boolean).join(' · ')}
            </p>
          </div>
        </div>
        <BookingProgress steps={PHASES.map((p) => t(`phases.${p}`))} current={PHASE_OF[step]} />
      </header>

      <section aria-labelledby="wizard-step" className="flex flex-1 flex-col gap-4 px-4 py-5 md:px-6">
        <h2 id="wizard-step" className="sr-only">
          {t(`titles.${step}`)}
        </h2>
        {blocked && (
          <InlineAlert
            tone="warning"
            title={t(blockedReason === 'shop.paused' ? 'blocked.paused' : 'blocked.other')}
          />
        )}
        {notice && (
          <InlineAlert
            tone={notice === 'conflict' ? 'danger' : 'warning'}
            title={t(`notices.${notice}.title`)}
          >
            {t(`notices.${notice}.body`)}
          </InlineAlert>
        )}

        {step === 'service' && (
          <>
            <ServiceStep offers={offers} value={items} onToggle={toggle} />
            {noCommonPro && <InlineAlert tone="warning" title={t('noCommonProfessional')} />}
          </>
        )}

        {step === 'professional' && (
          <fieldset className="flex min-w-0 flex-col gap-3">
            <legend className="sr-only">{t('titles.professional')}</legend>
            <RadioCard
              name="professional"
              value={ANY_PROFESSIONAL}
              checked={pro === ANY_PROFESSIONAL}
              onChange={() => go({ ...selection, pro: ANY_PROFESSIONAL }, 'professional', 'replace')}
              aside={
                <span className="rounded-badge bg-success-50 px-2 py-1 text-badge font-bold text-success-700">
                  {t('any.tag')}
                </span>
              }
            >
              <span className="flex items-center gap-3">
                <span
                  aria-hidden="true"
                  className="flex size-10 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700"
                >
                  <Icon name="star" className="size-5" />
                </span>
                <span>
                  <span className="block text-[0.9375rem] font-bold text-text-primary">{t('any.title')}</span>
                  <span className="block text-helper text-text-secondary">{t('any.body')}</span>
                </span>
              </span>
            </RadioCard>
            {eligible.length === 0 ? (
              <p className="text-helper text-text-secondary">{t('noProfessionals')}</p>
            ) : (
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                {eligible.map((p) => {
                  const name = localizedName(locale, p.nameAr, p.nameEn);
                  const specialty = locale === 'en' ? (p.specialtyEn ?? p.specialtyAr) : p.specialtyAr;
                  return (
                    <ProfessionalOption
                      key={p.id}
                      name="professional"
                      value={p.id}
                      checked={pro === p.id}
                      onChange={() => go({ ...selection, pro: p.id }, 'professional', 'replace')}
                      displayName={name}
                      specialty={specialty ?? undefined}
                      photoUrl={p.avatarUrl}
                      rating={p.reviewCount > 0 ? p.rating : null}
                      reviewCount={p.reviewCount}
                    />
                  );
                })}
              </div>
            )}
          </fieldset>
        )}

        {step === 'date' && (
          <div className="flex flex-col gap-4">
            <p className="flex items-center gap-2 text-helper text-text-secondary">
              <Icon name="clock" className="size-4" />
              {t('timeZoneNote')}
            </p>
            {dates.isPending ? (
              <SkeletonList rows={1} label={t('loading')} />
            ) : dates.isError ? (
              <LoadError onRetry={() => dates.refetch()} />
            ) : blocked ? null : (
              <>
                {nearestDate && nearestSlot && (
                  <div
                    className="flex flex-wrap items-center gap-3 rounded-card border-[1.5px] border-brand-500 bg-brand-50 p-4"
                    data-testid="nearest-slot"
                  >
                    <span
                      aria-hidden="true"
                      className="flex size-10 shrink-0 items-center justify-center rounded-full bg-surface text-brand-700"
                    >
                      <Icon name="clock" className="size-5" />
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-helper font-bold text-brand-700">{t('nearest.title')}</p>
                      <p className="text-[0.9375rem] font-bold text-text-primary">
                        {dayLabel(nearestDate)} · {formatTime(nearestSlot.startsAt, locale, shop.timeZone)}
                      </p>
                    </div>
                    <Button
                      size="md"
                      onClick={() =>
                        go({ ...selection, date: nearestDate, time: nearestSlot.localTime }, 'review')
                      }
                    >
                      {t('nearest.book')}
                    </Button>
                  </div>
                )}
                {nearestDate && nearestSlot && (
                  <p className="text-label font-bold text-text-strong">{t('nearest.orPick')}</p>
                )}
                <DateStrip
                  name="date"
                  today={today}
                  days={dates.data.dates.map((d) => ({ date: d.date, available: d.slotCount > 0 }))}
                  value={dateOk ? date : undefined}
                  onValueChange={(next) => {
                    scrollToTimes.current = true;
                    go(
                      { ...selection, date: next, time: next === date ? time : undefined },
                      'date',
                      'replace',
                    );
                  }}
                />
                {dateOk && (
                  <p role="status" className="text-helper font-bold text-text-strong">
                    {t('slotCount', { count: dates.data.dates.find((d) => d.date === date)?.slotCount ?? 0 })}
                  </p>
                )}
                {dateOk && (
                  <div ref={timesRef} className="flex scroll-mt-24 flex-col gap-4" data-testid="day-times">
                    {offer && (
                      <p className="flex items-center gap-2 rounded-card bg-brand-50 px-4 py-3 text-helper text-brand-700">
                        <Icon name="clock" className="size-4 shrink-0" />
                        {t('durationNote', {
                          service: offerName,
                          duration: formatDurationMinutes(offer.durationMinutes, locale),
                        })}
                      </p>
                    )}
                    {slots.isPending ? (
                      <SkeletonList rows={3} label={t('loading')} />
                    ) : slots.isError ? (
                      <LoadError onRetry={() => slots.refetch()} />
                    ) : slots.data.bookable === false ? null : (
                      <HourMinutePicker
                        key={date}
                        name="time"
                        timeZone={shop.timeZone}
                        slots={slots.data.slots.map((s) => ({ start: s.startsAt, localTime: s.localTime }))}
                        value={startsAt}
                        onValueChange={(start) => {
                          const slot = slots.data.slots.find((s) => s.startsAt === start);
                          if (slot) {
                            go({ ...selection, time: slot.localTime }, 'date', 'replace');
                          }
                        }}
                      />
                    )}
                  </div>
                )}
                {dates.data.dates.every((d) => d.slotCount === 0) && (
                  <InlineAlert tone="info" title={t('noDates')}>
                    {pro !== ANY_PROFESSIONAL ? t('tryAny') : null}
                  </InlineAlert>
                )}
                {pro !== ANY_PROFESSIONAL && dates.data.dates.some((d) => d.slotCount === 0) && (
                  <p className="text-helper text-text-secondary">{t('tryAny')}</p>
                )}
              </>
            )}
          </div>
        )}

        {step === 'review' && offer && (
          <div className="flex flex-col gap-4" data-testid="booking-review">
            <div className="flex items-center gap-3 rounded-card border border-border bg-surface p-4">
              <Avatar name={shopName} src={shop.logoUrl} size="md" />
              <div className="min-w-0">
                <p className="text-[0.9375rem] font-bold text-text-primary">{shopName}</p>
                {shop.area && <p className="text-helper text-text-secondary">{shop.area}</p>}
              </div>
            </div>
            <dl className="flex flex-col divide-y divide-border-row rounded-card border border-border bg-surface px-4">
              <SummaryRow
                icon="scissors"
                label={t(items.length > 1 ? 'summary.services' : 'summary.service')}
                value={
                  items.length > 1 ? (
                    <ul className="flex flex-col gap-0.5">
                      {items.map((item) => (
                        <li key={item.id}>{localizedName(locale, item.nameAr, item.nameEn)}</li>
                      ))}
                    </ul>
                  ) : (
                    offerName
                  )
                }
              />
              <SummaryRow
                icon="user"
                label={t('summary.professional')}
                value={chosenPro ? proName : t('summary.anyAssigned')}
              />
              <SummaryRow icon="calendar" label={t('summary.date')} value={dateLabel} />
              <SummaryRow
                icon="clock"
                label={t('summary.time')}
                value={
                  startsAt ? (
                    <span>
                      {timeLabel} —{' '}
                      {formatTime(endOf(startsAt, offer.durationMinutes), locale, shop.timeZone)}
                    </span>
                  ) : (
                    '…'
                  )
                }
              />
              <SummaryRow
                icon="info"
                label={t('summary.duration')}
                value={formatDurationMinutes(offer.durationMinutes, locale)}
              />
            </dl>
            <div className="flex items-center justify-between rounded-card bg-bg-subtle px-4 py-3">
              <span className="text-label font-bold text-text-strong">{t('summary.total')}</span>
              <span className="font-latin text-[1.125rem] font-extrabold text-navy-900">
                {formatPrice(offer.price, locale, offer.currency)}
              </span>
            </div>
            <TextareaField
              label={t('note.label')}
              optional
              placeholder={t('note.placeholder')}
              maxLength={NOTE_MAX}
              rows={3}
              value={note}
              onChange={(event) => setNote(event.target.value)}
            />
            <InlineAlert tone="info" title={t('policy.title')}>
              {t('policy.body', { window: t(`window.${cutoff.unit}`, { count: cutoff.count }) })}
            </InlineAlert>
            {viewer === 'guest' && <p className="text-helper text-text-secondary">{t('signInNote')}</p>}
            {viewer === 'staff' && <InlineAlert tone="warning" title={t('customersOnly')} />}
            {error && <InlineAlert tone="danger" title={error} />}
          </div>
        )}
      </section>

      <footer className="sticky bottom-0 z-10 border-t border-border bg-surface/95 backdrop-blur-md">
        <div className="flex items-center gap-4 px-4 py-3 md:px-6">
          <div className="min-w-0 flex-1">
            <p className="truncate text-helper text-text-secondary" data-testid="wizard-footer-note">
              {footerNote}
            </p>
            {offer && (
              <p className="font-latin text-[1.0625rem] font-extrabold text-navy-900">
                {formatPrice(offer.price, locale, offer.currency)}
              </p>
            )}
          </div>
          {step === 'review' ? (
            <Button
              size="lg"
              className="min-w-[160px]"
              loading={submitting}
              disabled={!startsAt || blocked || viewer === 'staff'}
              onClick={confirm}
            >
              {t('confirm')}
            </Button>
          ) : (
            <Button size="lg" className="min-w-[160px]" disabled={!canContinue} onClick={goNext}>
              {t('next')}
            </Button>
          )}
        </div>
      </footer>
    </div>
  );
}

function SummaryRow({ icon, label, value }: { icon: DesignIconName; label: string; value: ReactNode }) {
  return (
    <div className="flex items-center gap-3 py-3">
      <Icon name={icon} className="size-[18px] shrink-0 text-brand-700" />
      <dt className="text-helper text-text-secondary">{label}</dt>
      <dd className="ms-auto text-end text-label font-bold text-text-primary">{value}</dd>
    </div>
  );
}

function LoadError({ onRetry }: { onRetry: () => void }) {
  const t = useTranslations('booking');
  return (
    <InlineAlert
      tone="danger"
      title={t('loadError')}
      action={
        <Button variant="outline" size="sm" onClick={onRetry}>
          {t('retry')}
        </Button>
      }
    />
  );
}

/** Compact tiles (D-125): any number of services, booked together; a package is booked on its own. */
function ServiceStep({
  offers,
  value,
  onToggle,
}: {
  offers: WizardOffer[];
  value: WizardOffer[];
  onToggle: (offer: WizardOffer) => void;
}) {
  const t = useTranslations('booking');
  const locale = useLocale() as AppLocale;
  if (offers.length === 0) {
    return <p className="text-helper text-text-secondary">{t('noServices')}</p>;
  }
  const groups = (['service', 'package'] as const)
    .map((kind) => ({ kind, items: offers.filter((o) => o.kind === kind) }))
    .filter((group) => group.items.length > 0);
  return (
    <div className="flex flex-col gap-5">
      {groups.map((group) => (
        <fieldset key={group.kind} className="flex min-w-0 flex-col">
          <legend className="pb-1 text-label font-bold text-text-strong">
            {t(group.kind === 'service' ? 'services' : 'packages')}
          </legend>
          <p className="pb-2.5 text-helper text-text-secondary">
            {t(group.kind === 'service' ? 'servicesHint' : 'packagesHint')}
          </p>
          <div className="grid grid-cols-2 gap-2.5 md:grid-cols-3">
            {group.items.map((o) => {
              const description = localizedText(locale, o.descriptionAr, o.descriptionEn);
              return (
                <ServiceTile
                  key={`${o.kind}-${o.id}`}
                  name={group.kind === 'service' ? 'services' : 'package'}
                  value={`${o.kind}:${o.id}`}
                  checked={value.some((v) => v.kind === o.kind && v.id === o.id)}
                  onChange={() => onToggle(o)}
                  title={localizedName(locale, o.nameAr, o.nameEn)}
                  description={description?.text}
                  descriptionLang={description ? langIfOther(description, locale) : undefined}
                  price={o.price}
                  durationMinutes={o.durationMinutes}
                />
              );
            })}
          </div>
        </fieldset>
      ))}
    </div>
  );
}
