'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useEffect, useRef, useState } from 'react';
import { Avatar } from '@/components/ui/Avatar';
import { Button, IconButton } from '@/components/ui/Button';
import { DateStrip, SlotGrid, Stepper } from '@/components/ui/booking';
import { ProfessionalOption, ServiceOption } from '@/components/ui/cards';
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
  newIdempotencyKey,
  nextStep,
  previousStep,
  readWizardQuery,
  resolveStart,
  selectionFrom,
  WIZARD_STEPS,
  type WizardNotice,
  wizardNotice,
  type WizardOffer,
  type WizardSelection,
  type WizardStep,
  wizardPath,
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

function offerQuery(offer: WizardOffer, pro: string) {
  return {
    serviceId: offer.kind === 'service' ? offer.id : undefined,
    packageId: offer.kind === 'package' ? offer.id : undefined,
    professionalId: pro === ANY_PROFESSIONAL ? undefined : pro,
  };
}

/**
 * The booking wizard (c-booking, spec §12, D-028, D-096): service or package → professional or "any" → date → time →
 * review → confirmation. Only bookable dates and slots are offered (server-computed, D-009). Guests review everything
 * and sign in when they confirm; the wizard URL (with the review step) is the `returnTo`, so they come back to the same
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
  const { offer, pro, date, time } = selection;

  const notice = wizardNotice(query);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const keyed = useRef<KeyedIntent | undefined>(undefined);
  const headingRef = useRef<HTMLHeadingElement>(null);

  const go = (
    next: WizardSelection,
    to: WizardStep,
    mode: 'push' | 'replace' = 'push',
    reason?: WizardNotice,
  ) => {
    const url = `/${locale}${wizardPath(shop.slug, next, to, reason)}`;
    window.history[mode === 'push' ? 'pushState' : 'replaceState'](null, '', url);
  };

  // Move focus to the step heading when the step changes, so keyboard and screen-reader users follow along.
  const shownStep = useRef(step);
  useEffect(() => {
    if (shownStep.current !== step) {
      shownStep.current = step;
      headingRef.current?.focus();
    }
  }, [step]);

  const dates = useQuery({
    queryKey: ['booking-dates', shop.slug, offer?.kind, offer?.id, pro],
    enabled: Boolean(offer) && stepIndex >= 2,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/public/shops/{slug}/availability/dates', {
          params: { path: { slug: shop.slug }, query: offerQuery(offer!, pro) },
        }),
      ),
  });

  const slots = useQuery({
    queryKey: ['booking-slots', shop.slug, offer?.kind, offer?.id, pro, date],
    enabled: Boolean(offer && date) && stepIndex >= 3,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/public/shops/{slug}/availability/slots', {
          params: { path: { slug: shop.slug }, query: { ...offerQuery(offer!, pro), date: date! } },
        }),
      ),
  });

  const startsAt = resolveStart(slots.data?.slots, time);
  const blocked = dates.data?.bookable === false || slots.data?.bookable === false;
  const blockedReason = dates.data?.blockedReason ?? slots.data?.blockedReason;

  // A date or time from the URL (a shared link, the sign-in round trip) is checked against fresh availability.
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
      go({ ...selection, time: undefined }, 'time', 'replace', 'timeGone');
    }
    // `go` and `selection` are derived from the URL on every render; the data answers are what trigger the checks.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dates.data, slots.data, step, date, time, startsAt, blocked]);

  const eligible = eligibleProfessionals(offer, professionals);
  const chosenPro = professionals.find((p) => p.id === pro);
  const offerName = offer ? localizedName(locale, offer.nameAr, offer.nameEn) : '';
  const proName = chosenPro ? localizedName(locale, chosenPro.nameAr, chosenPro.nameEn) : t('any.title');
  const shopName = localizedName(locale, shop.nameAr, shop.nameEn);
  const cutoff = cutoffParts(shop.cancellationCutoffMinutes);
  const dateLabel = date
    ? formatLocalDate(date, locale, { weekday: 'long', day: 'numeric', month: 'long' })
    : '';
  const timeLabel = startsAt ? formatTime(startsAt, locale, shop.timeZone) : '';

  const canContinue =
    !blocked &&
    ((step === 'service' && Boolean(offer)) ||
      step === 'professional' ||
      (step === 'date' && Boolean(date)) ||
      (step === 'time' && Boolean(startsAt)));

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
      { offerId: offer.id, pro, startsAt, note: trimmed },
      newIdempotencyKey,
    );
    setSubmitting(true);
    try {
      const booking = ensureOk(
        await browserApi.POST('/api/v1/bookings', {
          params: { header: { 'Idempotency-Key': keyed.current.key } },
          body: {
            shopSlug: shop.slug,
            serviceId: offer.kind === 'service' ? offer.id : null,
            packageId: offer.kind === 'package' ? offer.id : null,
            professionalId: pro === ANY_PROFESSIONAL ? null : pro,
            startsAt,
            note: trimmed || null,
          },
        }),
      );
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
        go({ ...selection, time: undefined }, 'time', 'replace', 'conflict');
      } else if (failure.status === 403) {
        setError(t('customersOnly'));
      } else {
        setError(apiMessage(failure));
      }
    }
  };

  const footerNote =
    step === 'service'
      ? offerName
      : step === 'professional' || step === 'date'
        ? [offerName, proName].filter(Boolean).join(' · ')
        : step === 'time'
          ? [dateLabel, proName].filter(Boolean).join(' · ')
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
        <Stepper steps={WIZARD_STEPS.map((s) => t(`steps.${s}`))} current={stepIndex} />
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
          <ServiceStep
            offers={offers}
            value={offer}
            onChange={(next) =>
              go(
                {
                  offer: next,
                  pro: eligibleProfessionals(next, professionals).some((p) => p.id === pro)
                    ? pro
                    : ANY_PROFESSIONAL,
                },
                'service',
                'replace',
              )
            }
          />
        )}

        {step === 'professional' && (
          <fieldset className="flex min-w-0 flex-col gap-3">
            <legend className="sr-only">{t('titles.professional')}</legend>
            <RadioCard
              name="professional"
              value={ANY_PROFESSIONAL}
              checked={pro === ANY_PROFESSIONAL}
              onChange={() => go({ offer, pro: ANY_PROFESSIONAL, date }, 'professional', 'replace')}
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
                      onChange={() => go({ offer, pro: p.id, date }, 'professional', 'replace')}
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
                <DateStrip
                  name="date"
                  today={todayLocal(shop.timeZone)}
                  days={dates.data.dates.map((d) => ({ date: d.date, available: d.slotCount > 0 }))}
                  value={date}
                  onValueChange={(next) => {
                    go({ offer, pro, date: next }, 'date', 'replace');
                  }}
                />
                {date && (
                  <p role="status" className="text-helper font-bold text-text-strong">
                    {t('slotCount', { count: dates.data.dates.find((d) => d.date === date)?.slotCount ?? 0 })}
                  </p>
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

        {step === 'time' && (
          <div className="flex flex-col gap-4">
            {offer && (
              <p className="flex items-center gap-2 rounded-card bg-brand-50 px-4 py-3 text-helper text-brand-700">
                <Icon name="clock" className="size-4 shrink-0" />
                {t('durationNote', {
                  service: offerName,
                  duration: formatDurationMinutes(offer.durationMinutes, locale),
                })}
              </p>
            )}
            {date && <p className="text-label font-bold text-text-primary">{dateLabel}</p>}
            {slots.isPending ? (
              <SkeletonList rows={3} label={t('loading')} />
            ) : slots.isError ? (
              <LoadError onRetry={() => slots.refetch()} />
            ) : blocked ? null : (
              <SlotGrid
                name="time"
                timeZone={shop.timeZone}
                slots={slots.data.slots.map((s) => ({ start: s.startsAt }))}
                value={startsAt}
                onValueChange={(start) => {
                  const slot = slots.data.slots.find((s) => s.startsAt === start);
                  if (slot) {
                    go({ offer, pro, date, time: slot.localTime }, 'time', 'replace');
                  }
                }}
              />
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
              <SummaryRow icon="scissors" label={t('summary.service')} value={offerName} />
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

function ServiceStep({
  offers,
  value,
  onChange,
}: {
  offers: WizardOffer[];
  value: WizardOffer | undefined;
  onChange: (offer: WizardOffer) => void;
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
        <fieldset key={group.kind} className="flex min-w-0 flex-col gap-2.5">
          <legend className="pb-2 text-label font-bold text-text-strong">
            {t(group.kind === 'service' ? 'services' : 'packages')}
          </legend>
          {group.items.map((o) => {
            const description = localizedText(locale, o.descriptionAr, o.descriptionEn);
            return (
              <ServiceOption
                key={`${o.kind}-${o.id}`}
                name="offer"
                value={`${o.kind}:${o.id}`}
                checked={value?.kind === o.kind && value.id === o.id}
                onChange={() => onChange(o)}
                title={localizedName(locale, o.nameAr, o.nameEn)}
                description={description?.text}
                descriptionLang={description ? langIfOther(description, locale) : undefined}
                price={o.price}
                durationMinutes={o.durationMinutes}
              />
            );
          })}
        </fieldset>
      ))}
    </div>
  );
}
