'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'next/navigation';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useEffect, useRef, useState } from 'react';
import { type CatalogSection, ServiceCatalog } from '@/components/booking/ServiceCatalog';
import { useLoyaltyRewardName } from '@/components/loyalty/LoyaltyParts';
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
import type { components } from '@/lib/api/schema';
import { withReturnTo } from '@/lib/auth/paths';
import {
  ANY_PROFESSIONAL,
  blockingItem,
  couponDiscount,
  currentStep,
  durationFor,
  eligibleProfessionals,
  keyFor,
  type KeyedIntent,
  newIdempotencyKey,
  nextStep,
  offerKey,
  offersFor,
  previousStep,
  readWizardQuery,
  resolveStart,
  restoreSelection,
  rewardFit,
  selectionFrom,
  toggleItem,
  WIZARD_STEPS,
  type WizardNotice,
  wizardNotice,
  type WizardOffer,
  type WizardPointsReward,
  type WizardSelection,
  type WizardStep,
  wizardPath,
  withItems,
} from '@/lib/booking/wizard';
import { cutoffParts, endOf } from '@/lib/booking/format';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatPrice,
  formatTime,
} from '@/lib/i18n/format';
import { formatLocalDate, todayLocal } from '@/lib/i18n/localDate';
import { languagesLine } from '@/lib/i18n/languages';
import { localizedName } from '@/lib/i18n/localized';

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
  /** The languages the barber speaks (D-149), shown on their card (D-155). */
  languages: string[];
  /** Marked «غير متوفر اليوم» (D-155): the card is disabled today, and «أي مختص» never picks them. */
  unavailableToday?: boolean;
};

/** Who is looking: a guest confirms after signing in (D-096); staff accounts cannot book. */
export type WizardViewer = 'guest' | 'customer' | 'staff';

/**
 * A D-132 loyalty coupon the customer can still use at this shop, offered on the review step. Since D-152 it lowers the
 * price like the points reward: a free-service coupon makes the booking free, a percent coupon takes its percent off.
 */
export type WizardReward = {
  id: string;
  titleAr: string;
  titleEn: string | null;
  kind: 'FreeService' | 'Discount';
  discountPercent: number | null;
};

/** The review step's reward choice: none, the points reward (D-149), or a D-132 coupon's id. */
const POINTS_REWARD = 'points';

const NOTE_MAX = 500;

/** The customer's upcoming booking that stops a new one (D-155, «حجز واحد فعّال بس للزبون، في كل المحلات»). */
export type WizardActiveBooking = components['schemas']['CustomerActiveBookingResponse'];

/**
 * «عندك حجز قادم» with a link to it (D-155, the client's 3-2): one upcoming booking per customer across the shops, so a
 * new one waits until it is cancelled, done («انتهت الخدمة») or marked «ما حضر». Its time can still be changed from its
 * page. The server has the last word (409 booking.active_booking_exists).
 */
function ActiveBookingWarning({ booking }: { booking: WizardActiveBooking }) {
  const t = useTranslations('booking.activeBooking');
  const locale = useLocale() as AppLocale;
  return (
    <div data-testid="active-booking">
      <InlineAlert
        tone="warning"
        title={t('title')}
        action={
          <Link
            href={`/account/bookings/${booking.id}`}
            className="inline-flex min-h-11 items-center text-label font-bold text-text-link underline"
          >
            {t('view')}
          </Link>
        }
      >
        {t('body', {
          shop: localizedName(locale, booking.shop.nameAr, booking.shop.nameEn),
          date: formatDate(booking.startsAt, locale),
          time: formatTime(booking.startsAt, locale),
        })}
      </InlineAlert>
    </div>
  );
}

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

/**
 * The booking in progress also survives leaving the page or a reload (D-148, «٤-٧»), until it is made or emptied: a
 * first-party cookie for the last shop (Web Storage stays banned, spec §9). It holds ids, the date and the time only.
 * Only a bare link picks it up (D-157): every «احجز» button names where it starts, so it begins a new booking.
 */
const DRAFT_COOKIE = 'gh-booking-draft';
const DRAFT_DAYS = 7;

function remember(slug: string, selection: WizardSelection) {
  const query =
    selection.items.length === 0 ? '' : (wizardPath(slug, selection, 'service').split('?')[1] ?? '');
  if (query) lastChoices.set(slug, query);
  else lastChoices.delete(slug);
  const secure = window.location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = query
    ? `${DRAFT_COOKIE}=${encodeURIComponent(`${slug}|${query}`)}; Path=/; Max-Age=${DRAFT_DAYS * 86_400}; SameSite=Lax${secure}`
    : `${DRAFT_COOKIE}=; Path=/; Max-Age=0; SameSite=Lax${secure}`;
}

function recall(slug: string): string | undefined {
  const memory = lastChoices.get(slug);
  if (memory) return memory;
  const raw = document.cookie
    .split('; ')
    .find((c) => c.startsWith(`${DRAFT_COOKIE}=`))
    ?.slice(DRAFT_COOKIE.length + 1);
  if (!raw) return undefined;
  const [draftSlug, query] = decodeURIComponent(raw).split('|');
  return draftSlug === slug ? query : undefined;
}

/**
 * The booking wizard (c-booking, spec §12, D-028, D-096, D-125): one or more services or a package → professional or
 * "any" → date (with the nearest free time offered first) → hour, then minutes → review → confirmation. Opened from a
 * professional's page or QR code, it lists only their services and goes from the services to the time (D-133). Only
 * bookable dates and slots are offered (server-computed, D-009). Going back never drops a choice. Guests review
 * everything and sign in when they confirm; the wizard URL (with the review step) is the `returnTo`, so they come back
 * to the same choice, which is checked again. The submit carries an idempotency key, and a time taken meanwhile sends
 * the customer back to fresh slots.
 */
export function BookingWizard({
  shop,
  offers,
  professionals,
  viewer,
  rewards = [],
  pointsReward = null,
  sections = [],
  activeBooking = null,
}: {
  shop: WizardShop;
  offers: WizardOffer[];
  professionals: WizardPro[];
  viewer: WizardViewer;
  rewards?: WizardReward[];
  /** A full points card ready to use here (D-149, «استخدم مكافأتي»), or null. */
  pointsReward?: WizardPointsReward | null;
  /** The shop's own sections, in the owner's order: the service step's tabs (D-148, D-149). */
  sections?: CatalogSection[];
  /** The customer's upcoming booking at any shop, which stops a new one (D-155), or null. */
  activeBooking?: WizardActiveBooking | null;
}) {
  const t = useTranslations('booking');
  const tl = useTranslations('loyalty.wizard');
  const tSpoken = useTranslations('spokenLanguages');
  const tDay = useTranslations('booking.professionalDay');
  const rewardName = useLoyaltyRewardName();
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
  // A link from the loyalty card ticks its reward (D-152, 3-5): `reward=points` or a coupon's id.
  const [rewardId, setRewardId] = useState(() =>
    query.reward === POINTS_REWARD || rewards.some((r) => r.id === query.reward) ? (query.reward ?? '') : '',
  );
  // The points reward fits only some choices (D-149): never with a package, a free service only once it is chosen. A
  // choice that stopped fitting (the services changed) falls back to none.
  const fit =
    viewer === 'customer' && pointsReward && offer ? rewardFit(items, offer.price, pointsReward) : null;
  const usingPoints = rewardId === POINTS_REWARD && Boolean(fit?.usable);
  const coupon = rewards.find((r) => r.id === rewardId);
  // What the reward takes off (D-152, 1-3): the points reward's, or a D-132 coupon's.
  const discount =
    usingPoints && fit?.usable
      ? fit.discount
      : coupon && offer
        ? couponDiscount(offer.price, coupon)
        : undefined;
  // The service the reward makes free, shown as «مجانية» on its line (the client's «حلاقة شعر ٦٠ ← مجانية»).
  const freeItemId =
    usingPoints && pointsReward?.rewardType === 'FreeService' ? pointsReward.rewardServiceId : null;
  const due = offer ? Math.max(offer.price - (discount ?? 0), 0) : 0;
  const discountLabel =
    discount !== undefined && offer ? `−${formatPrice(discount, locale, offer.currency)}` : '';
  const choice = rewardId === POINTS_REWARD && !usingPoints ? '' : rewardId;
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  // A time refused at confirmation (D-152, the client's 1-2): it fell inside the shop's lead time meanwhile, or another
  // customer took it. The review stays where it is and offers the nearest times again.
  const [conflict, setConflict] = useState<'tooSoon' | 'taken'>();
  // One upcoming booking per customer (D-155): known when the page loaded, or learnt from a 409 at the confirm.
  const [active, setActive] = useState(activeBooking);
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

  // Going back must never drop a choice (D-125): a history entry from before the later steps were chosen (the browser's
  // or phone's back button) gets them back. A link with no query at all picks up the shop's draft. Every «احجز» button
  // names where it starts (the shop page's «احجز الآن» sends `step=service`), so it is a new booking and replaces the
  // draft: the client's «يرجعني نفس القديمه» (D-157).
  useEffect(() => {
    const previous = latest.current;
    if (!previous) {
      latest.current = selection;
      const stored = recall(shop.slug);
      if (searchParams.toString() === '' && stored) {
        const restored = selectionFrom(readWizardQuery(new URLSearchParams(stored)), offers, professionals);
        // On the service step, with the earlier choices ticked: the customer sees them before going on (D-157, the
        // client's «يوديني على اختيار الوقت بدون ما اختار»), never straight to the time.
        if (restored.items.length > 0) go(restored, 'service', 'replace');
      } else {
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
  const nearest = useQuery(slotsOf(nearestDate, step === 'date' || Boolean(conflict)));
  const nearestSlot = nearest.data?.bookable === false ? undefined : nearest.data?.slots[0];

  const startsAt = resolveStart(slots.data?.slots, time);
  const blocked = dates.data?.bookable === false || slots.data?.bookable === false;
  const blockedReason = dates.data?.blockedReason ?? slots.data?.blockedReason;
  const dateOk = Boolean(date && dates.data?.dates.some((d) => d.date === date && d.slotCount > 0));

  // A date or time from the URL (a shared link, the sign-in round trip, a change of services) is checked against fresh
  // availability.
  useEffect(() => {
    // After a refused time the review offers the fresh ones itself (D-152).
    if (blocked || conflict) return;
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
  }, [dates.data, slots.data, step, date, time, startsAt, blocked, conflict]);

  // The nearest times after a refused one: the rest of that day, else the next day with a free time.
  const alternatives: { date: string; startsAt: string; localTime: string }[] = !conflict
    ? []
    : slots.data && date && slots.data.slots.length > 0
      ? slots.data.slots.slice(0, 4).map((s) => ({ date, startsAt: s.startsAt, localTime: s.localTime }))
      : nearestDate && nearest.data
        ? nearest.data.slots
            .slice(0, 4)
            .map((s) => ({ date: nearestDate, startsAt: s.startsAt, localTime: s.localTime }))
        : [];
  const alternativesLoading =
    Boolean(conflict) && (slots.isFetching || dates.isFetching || nearest.isFetching);

  useEffect(() => {
    if (scrollToTimes.current && dateOk) {
      scrollToTimes.current = false;
      timesRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [date, dateOk]);

  const eligible = eligibleProfessionals(offer, professionals);
  const noCommonPro = items.length > 1 && eligible.length === 0;
  // Which choice leaves no single barber for the rest (D-148, «٤-٨»).
  const blocking = noCommonPro ? blockingItem(items) : undefined;
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
  // The chosen barber's own lengths (D-149); the standard ones for «أي مختص».
  const duration = durationFor(items, pro);
  const itemsLabel =
    items.length > 1
      ? `${t('selectedCount', { count: items.length })} — ${formatDurationMinutes(duration, locale)}`
      : offerName;

  const canContinue =
    !blocked &&
    ((step === 'service' && Boolean(offer) && !noCommonPro) ||
      step === 'professional' ||
      (step === 'date' && dateOk && Boolean(startsAt)));

  const goNext = () => {
    const to = nextStep(step, pro);
    if (to) go(selection, to);
  };

  const goBack = () => {
    const to = previousStep(step);
    if (to) {
      setError(undefined);
      go(selection, to);
    }
  };

  // A package with other services in one booking (D-148); a service inside the chosen package is refused with a message,
  // never by dropping the package.
  const [catalogNote, setCatalogNote] = useState<string>();
  const toggle = (item: WizardOffer) => {
    const result = toggleItem(items, item);
    const name = (o: WizardOffer) => localizedName(locale, o.nameAr, o.nameEn);
    setCatalogNote(
      result.inPackage
        ? t('catalog.inPackage', { service: name(item), package: name(result.inPackage) })
        : result.dropped.length > 0
          ? t('catalog.dropped', {
              services: result.dropped.map(name).join(locale === 'ar' ? '، ' : ', '),
              package: name(item),
            })
          : undefined,
    );
    if (!result.inPackage) go(withItems(selection, result.items, professionals), 'service', 'replace');
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

    setConflict(undefined);
    const trimmed = note.trim();
    keyed.current = keyFor(
      keyed.current,
      { offerId: itemsKey, pro, startsAt, note: trimmed, rewardId: choice },
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
            // One reward per booking (D-149): the points reward, or a D-132 coupon.
            useLoyaltyReward: usingPoints,
            loyaltyRewardId: choice && choice !== POINTS_REWARD ? choice : null,
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
      } else if (
        failure.errorCode === 'booking.slot_unavailable' ||
        failure.errorCode === 'booking.slot_too_soon'
      ) {
        // Stay on the review and offer the nearest times now (D-152, 1-2), instead of sending the customer back.
        setConflict(failure.errorCode === 'booking.slot_too_soon' ? 'tooSoon' : 'taken');
        await queryClient.invalidateQueries({ queryKey: ['booking-slots'] });
        await queryClient.invalidateQueries({ queryKey: ['booking-dates'] });
      } else if (failure.errorCode === 'booking.active_booking_exists') {
        const found = await browserApi.GET('/api/v1/me/bookings/active').catch(() => undefined);
        if (found?.data) setActive(found.data);
        else setError(apiMessage(failure));
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
        ? [itemsLabel, proName].filter(Boolean).join(' — ')
        : [dateLabel, timeLabel].filter(Boolean).join(' — ');

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
              {[shopName, shop.area].filter(Boolean).join(' — ')}
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
        {active && <ActiveBookingWarning booking={active} />}
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
            {chosenPro && (
              <div
                className="flex items-center gap-3 rounded-card border border-border bg-surface p-3"
                data-testid="wizard-chosen-pro"
              >
                <Avatar name={proName} src={chosenPro.avatarUrl} size="sm" />
                <div className="min-w-0 flex-1">
                  <p className="text-label font-bold text-text-primary">
                    {t('withPro.title', { name: proName })}
                  </p>
                  <p className="text-helper text-text-secondary">{t('withPro.body')}</p>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => go({ ...selection, pro: ANY_PROFESSIONAL }, 'service', 'replace')}
                >
                  {t('withPro.change')}
                </Button>
              </div>
            )}
            <ServiceStep
              opened={query.section}
              offers={offersFor(offers, pro)}
              value={items}
              onToggle={toggle}
              sections={sections}
            />
            {catalogNote && (
              <p role="status" className="rounded-card bg-warning-50 p-3 text-label text-warning-700">
                {catalogNote}
              </p>
            )}
            {noCommonPro && (
              <InlineAlert
                tone="warning"
                title={
                  blocking
                    ? t('noCommonProfessionalFor', {
                        service: localizedName(locale, blocking.nameAr, blocking.nameEn),
                      })
                    : t('noCommonProfessional')
                }
              />
            )}
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
                  const specialty = localizedName(locale, p.specialtyAr ?? '', p.specialtyEn) || null;
                  // A barber whose own length differs says it on their card (D-149: «٤٥ دقيقة»).
                  const own = durationFor(items, p.id);
                  const detail = [
                    specialty,
                    own !== durationFor(items, ANY_PROFESSIONAL) ? formatDurationMinutes(own, locale) : null,
                  ]
                    .filter(Boolean)
                    .join(' — ');
                  return (
                    <ProfessionalOption
                      key={p.id}
                      name="professional"
                      value={p.id}
                      checked={pro === p.id}
                      onChange={() => go({ ...selection, pro: p.id }, 'professional', 'replace')}
                      displayName={name}
                      specialty={detail || undefined}
                      photoUrl={p.avatarUrl}
                      rating={p.reviewCount > 0 ? p.rating : null}
                      reviewCount={p.reviewCount}
                      languages={languagesLine(p.languages, tSpoken, locale)}
                      unavailable={p.unavailableToday === true && pro !== p.id}
                      unavailableLabel={tDay('unavailableToday')}
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
                        {dayLabel(nearestDate)} — {formatTime(nearestSlot.startsAt, locale, shop.timeZone)}
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
                  days={dates.data.dates.map((d) => ({
                    date: d.date,
                    available: d.slotCount > 0,
                    closed: d.closed,
                  }))}
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
                          duration: formatDurationMinutes(duration, locale),
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
                        open={slots.data.open}
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
            {conflict && (
              <div className="flex flex-col gap-3" data-testid="booking-conflict" data-kind={conflict}>
                <InlineAlert tone="danger" title={t(`conflicts.${conflict}.title`)}>
                  {t(`conflicts.${conflict}.body`)}
                </InlineAlert>
                {alternatives.length > 0 ? (
                  <ul className="flex flex-wrap gap-2" aria-label={t('conflicts.label')}>
                    {alternatives.map((alt) => (
                      <li key={alt.startsAt}>
                        <Button
                          variant="outline"
                          size="md"
                          onClick={() => {
                            setConflict(undefined);
                            go({ ...selection, date: alt.date, time: alt.localTime }, 'review', 'replace');
                          }}
                        >
                          {alt.date === date ? '' : `${dayLabel(alt.date)} — `}
                          {formatTime(alt.startsAt, locale, shop.timeZone)}
                        </Button>
                      </li>
                    ))}
                  </ul>
                ) : alternativesLoading ? (
                  <p className="text-helper text-text-secondary">{t('conflicts.loading')}</p>
                ) : (
                  <Button
                    variant="outline"
                    size="md"
                    className="self-start"
                    onClick={() => {
                      setConflict(undefined);
                      go({ ...selection, time: undefined }, 'date', 'replace');
                    }}
                  >
                    {t('conflicts.otherDay')}
                  </Button>
                )}
              </div>
            )}
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
                      {timeLabel} — {formatTime(endOf(startsAt, duration), locale, shop.timeZone)}
                    </span>
                  ) : (
                    '…'
                  )
                }
              />
              <SummaryRow
                icon="info"
                label={t('summary.duration')}
                value={formatDurationMinutes(duration, locale)}
              />
            </dl>
            {/* D-152 (1-3): each service's price when there are several or one is free («مجانية»), the reward's
                discount, and the total the customer pays at the shop, net of the reward. */}
            <div
              className="flex flex-col gap-1.5 rounded-card bg-bg-subtle px-4 py-3"
              data-testid="booking-total"
            >
              {(items.length > 1 || freeItemId) && (
                <ul
                  className="flex flex-col gap-1 border-b border-border-row pb-2"
                  data-testid="booking-lines"
                >
                  {items.map((item) => {
                    const free = item.kind === 'service' && item.id === freeItemId;
                    return (
                      <li key={item.id} className="flex items-center justify-between gap-3 text-label">
                        <span className="min-w-0 text-text-strong">
                          {localizedName(locale, item.nameAr, item.nameEn)}
                        </span>
                        {free ? (
                          <span className="flex shrink-0 items-center gap-2" data-testid="free-line">
                            <s className="font-latin text-text-secondary">
                              {formatPrice(item.price, locale, offer.currency)}
                            </s>
                            <span className="font-bold text-success-700">{tl('free')}</span>
                          </span>
                        ) : (
                          <span className="shrink-0 font-latin font-bold text-text-primary">
                            {formatPrice(item.price, locale, offer.currency)}
                          </span>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
              {discount !== undefined && !freeItemId && (
                <div className="flex items-center justify-between text-label text-success-700">
                  <span>{tl('discount')}</span>
                  <span className="font-latin font-bold" data-testid="loyalty-discount">
                    {discountLabel}
                  </span>
                </div>
              )}
              <div className="flex items-center justify-between gap-3">
                <span className="text-label font-bold text-text-strong">{t('summary.total')}</span>
                <span className="flex items-baseline gap-2" data-testid="loyalty-due">
                  {discount !== undefined && (
                    <s className="font-latin text-label font-bold text-text-secondary">
                      {formatPrice(offer.price, locale, offer.currency)}
                    </s>
                  )}
                  <span className="font-latin text-[1.125rem] font-extrabold text-navy-900">
                    {formatPrice(due, locale, offer.currency)}
                  </span>
                </span>
              </div>
            </div>
            {viewer === 'customer' && (rewards.length > 0 || pointsReward) && (
              <fieldset className="flex flex-col gap-2" data-testid="loyalty-picker">
                <legend className="mb-1 flex items-center gap-2 text-label font-bold text-success-700">
                  <Icon name="tag" className="size-4" />
                  {tl('title')}
                </legend>
                {pointsReward && fit && (
                  <RadioCard
                    name="loyaltyReward"
                    value={POINTS_REWARD}
                    checked={choice === POINTS_REWARD}
                    disabled={!fit.usable}
                    onChange={() => setRewardId(POINTS_REWARD)}
                    data-testid="use-my-reward"
                  >
                    <span className="flex flex-col gap-0.5">
                      <span>{tl('usePoints', { reward: rewardName(pointsReward) })}</span>
                      {!fit.usable && (
                        <span className="text-helper text-text-secondary">
                          {fit.reason === 'package'
                            ? tl('notWithPackage')
                            : tl('needsService', {
                                service: localizedName(
                                  locale,
                                  pointsReward.rewardServiceNameAr ?? '',
                                  pointsReward.rewardServiceNameEn,
                                ),
                              })}
                        </span>
                      )}
                    </span>
                  </RadioCard>
                )}
                {rewards.map((reward) => (
                  <RadioCard
                    key={reward.id}
                    name="loyaltyReward"
                    value={reward.id}
                    checked={choice === reward.id}
                    onChange={() => setRewardId(reward.id)}
                  >
                    {tl('useCoupon', { reward: localizedName(locale, reward.titleAr, reward.titleEn) })}
                  </RadioCard>
                ))}
                <RadioCard
                  name="loyaltyReward"
                  value=""
                  checked={choice === ''}
                  onChange={() => setRewardId('')}
                >
                  {tl('none')}
                </RadioCard>
                <p className="text-helper text-text-secondary">
                  {(choice !== '' && choice !== POINTS_REWARD) || !pointsReward
                    ? tl('couponHint')
                    : tl('pointsHint')}
                </p>
              </fieldset>
            )}
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
              {shop.cancellationCutoffMinutes === 0
                ? t('policy.bodyUntilStart')
                : t('policy.body', { window: t(`window.${cutoff.unit}`, { count: cutoff.count }) })}
            </InlineAlert>
            {viewer === 'guest' && <p className="text-helper text-text-secondary">{t('signInNote')}</p>}
            {viewer === 'staff' && <InlineAlert tone="warning" title={t('customersOnly')} />}
            {error && <InlineAlert tone="danger" title={error} />}
          </div>
        )}
      </section>

      {/* At the bottom of the screen on every step (D-157, the client's «خلها تحت بالصفحه»): the root fills the height
          (`PublicShell fill`) and the step above it grows, so a short step no longer leaves the bar halfway up; on a long
          one it stays stuck to the bottom. Clear of the phone's home bar (D-152). */}
      <footer className="sticky bottom-0 z-10 border-t border-border bg-surface/95 pb-[env(safe-area-inset-bottom)] backdrop-blur-md">
        <div className="flex items-center gap-4 px-4 py-3 md:px-6">
          <div className="min-w-0 flex-1">
            <p className="truncate text-helper text-text-secondary" data-testid="wizard-footer-note">
              {footerNote}
            </p>
            {offer && (
              <p
                className="font-latin text-[1.0625rem] font-extrabold text-navy-900"
                data-testid="wizard-footer-price"
              >
                {formatPrice(step === 'review' ? due : offer.price, locale, offer.currency)}
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

/**
 * The service step (D-125, D-148): the shop's services in its own section tabs, packages last, as tiles; several services and
 * a package can be booked together. The tabs replace D-130's «كل الخدمات».
 */
function ServiceStep({
  offers,
  value,
  onToggle,
  sections,
  opened,
}: {
  offers: WizardOffer[];
  value: WizardOffer[];
  onToggle: (offer: WizardOffer) => void;
  sections: CatalogSection[];
  /** The tab to open while nothing is chosen: the one the shop page was showing (D-157). */
  opened?: string;
}) {
  const t = useTranslations('booking');
  if (offers.length === 0) {
    return <p className="text-helper text-text-secondary">{t('noServices')}</p>;
  }
  return (
    <ServiceCatalog
      items={offers}
      sections={sections}
      selected={value}
      opened={opened}
      choice={({ item, name, description, badge, checked }) => (
        <ServiceTile
          name={item.kind === 'service' ? 'services' : 'package'}
          value={`${item.kind}:${item.id}`}
          checked={checked}
          onChange={() => onToggle(offers.find((o) => o.kind === item.kind && o.id === item.id)!)}
          title={name}
          description={description?.text}
          descriptionLang={description?.lang}
          price={item.price}
          durationMinutes={item.durationMinutes}
          badge={badge}
        />
      )}
      label={t('services')}
    />
  );
}
