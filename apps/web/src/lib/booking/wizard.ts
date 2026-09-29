/**
 * Booking wizard state (D-028, D-096). The URL is the source of truth, so a step survives a reload, the back button
 * and the sign-in round trip: `service` or `package`, `pro` (a professional id or `any`), `date` (YYYY-MM-DD in the
 * shop's time zone), `time` (local HH:mm, as the professional page's time chips link it) and `step`.
 * Everything here is pure; the wizard component fetches the data and calls these.
 */

export const WIZARD_STEPS = ['service', 'professional', 'date', 'time', 'review'] as const;
export type WizardStep = (typeof WIZARD_STEPS)[number];

export const ANY_PROFESSIONAL = 'any';

export type WizardQuery = {
  service?: string;
  package?: string;
  pro?: string;
  date?: string;
  time?: string;
  step?: string;
  /** Why the wizard moved the customer back (a time taken meanwhile, a day no longer free). */
  notice?: string;
};

export const WIZARD_NOTICES = ['timeGone', 'dateGone', 'conflict'] as const;
export type WizardNotice = (typeof WIZARD_NOTICES)[number];

export function wizardNotice(query: WizardQuery): WizardNotice | null {
  return (WIZARD_NOTICES as readonly string[]).includes(query.notice ?? '')
    ? (query.notice as WizardNotice)
    : null;
}

export type WizardOffer = {
  kind: 'service' | 'package';
  id: string;
  nameAr: string;
  nameEn: string | null;
  descriptionAr: string | null;
  descriptionEn: string | null;
  price: number;
  currency: string;
  durationMinutes: number;
  /** Professionals assigned to the service (or to every service of the package). */
  professionalIds: string[];
};

export type WizardProfessional = { id: string };

/** The selections the URL carries after validation against the shop's data. */
export type WizardSelection = {
  offer?: WizardOffer;
  /** A professional id, or {@link ANY_PROFESSIONAL}. */
  pro: string;
  date?: string;
  time?: string;
};

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

type Params = URLSearchParams | Record<string, string | string[] | undefined>;

function read(params: Params, key: string): string | undefined {
  if (params instanceof URLSearchParams) return params.get(key) ?? undefined;
  const value = params[key];
  return (Array.isArray(value) ? value[0] : value) || undefined;
}

export function readWizardQuery(params: Params): WizardQuery {
  return {
    service: read(params, 'service'),
    package: read(params, 'package'),
    pro: read(params, 'pro'),
    date: read(params, 'date'),
    time: read(params, 'time'),
    step: read(params, 'step'),
    notice: read(params, 'notice'),
  };
}

/** The professionals who can do the offer, in the shop's order. */
export function eligibleProfessionals<P extends WizardProfessional>(
  offer: WizardOffer | undefined,
  pros: P[],
): P[] {
  if (!offer) return [];
  const eligible = new Set(offer.professionalIds);
  return pros.filter((p) => eligible.has(p.id));
}

/**
 * Keeps only what is valid for this shop: a known offer, a professional who does it (else "any"), a well-formed date
 * and time. Whether the date and time are still free is checked against the availability answers later.
 */
export function selectionFrom(
  query: WizardQuery,
  offers: WizardOffer[],
  pros: WizardProfessional[],
): WizardSelection {
  const offer = query.package
    ? offers.find((o) => o.kind === 'package' && o.id === query.package)
    : query.service
      ? offers.find((o) => o.kind === 'service' && o.id === query.service)
      : undefined;
  const pro =
    query.pro && eligibleProfessionals(offer, pros).some((p) => p.id === query.pro)
      ? query.pro
      : ANY_PROFESSIONAL;
  const date = offer && query.date && DATE.test(query.date) ? query.date : undefined;
  const time = date && query.time && TIME.test(query.time) ? query.time : undefined;
  return { offer, pro, date, time };
}

/** The first step whose choice is missing: nothing after it can be shown yet. */
export function firstIncompleteStep(selection: WizardSelection): WizardStep {
  if (!selection.offer) return 'service';
  if (!selection.date) return 'date';
  if (!selection.time) return 'time';
  return 'review';
}

/**
 * The step to show. An explicit `step` is honoured up to the first incomplete one; without it (links from the shop and
 * professional pages) the wizard opens at the furthest step the link implies: a service goes to the professional step
 * ("any" preselected), a service with a date to the time step, a full link to the review.
 */
export function currentStep(query: WizardQuery, selection: WizardSelection): WizardStep {
  const limit = WIZARD_STEPS.indexOf(firstIncompleteStep(selection));
  const asked = WIZARD_STEPS.indexOf(query.step as WizardStep);
  if (asked >= 0) return WIZARD_STEPS[Math.min(asked, limit)]!;
  if (!selection.offer) return 'service';
  if (!selection.date) return 'professional';
  return WIZARD_STEPS[limit]!;
}

export function previousStep(step: WizardStep): WizardStep | undefined {
  const index = WIZARD_STEPS.indexOf(step);
  return index > 0 ? WIZARD_STEPS[index - 1] : undefined;
}

export function nextStep(step: WizardStep): WizardStep | undefined {
  return WIZARD_STEPS[WIZARD_STEPS.indexOf(step) + 1];
}

/** The wizard's locale-less path for a selection and step (what `returnTo` and the history entries hold). */
export function wizardPath(
  slug: string,
  selection: WizardSelection,
  step: WizardStep,
  notice?: WizardNotice,
): string {
  const params = new URLSearchParams();
  if (selection.offer) params.set(selection.offer.kind, selection.offer.id);
  if (selection.offer && selection.pro !== ANY_PROFESSIONAL) params.set('pro', selection.pro);
  if (selection.date) params.set('date', selection.date);
  if (selection.time) params.set('time', selection.time);
  params.set('step', step);
  if (notice) params.set('notice', notice);
  return `/shops/${encodeURIComponent(slug)}/book?${params.toString()}`;
}

/** What a booking request is about; a new idempotency key is used whenever any of it changes. */
export type BookingIntent = { offerId: string; pro: string; startsAt: string; note: string };

export type KeyedIntent = { intent: BookingIntent; key: string };

function sameIntent(a: BookingIntent, b: BookingIntent): boolean {
  return a.offerId === b.offerId && a.pro === b.pro && a.startsAt === b.startsAt && a.note === b.note;
}

/**
 * The idempotency key for a submission (R-BKG-05): the same key while the request is the same (a double click or a
 * retry after a network error replays the first booking), a new one as soon as the offer, professional, time or note
 * changes (the API refuses a reused key with another body).
 */
export function keyFor(
  previous: KeyedIntent | undefined,
  intent: BookingIntent,
  generate: () => string,
): KeyedIntent {
  return previous && sameIntent(previous.intent, intent) ? previous : { intent, key: generate() };
}

/** The local start matching the URL's time, from the slots of the chosen date. */
export function resolveStart<S extends { localTime: string; startsAt: string }>(
  slots: S[] | undefined,
  time: string | undefined,
): string | undefined {
  return time ? slots?.find((slot) => slot.localTime === time)?.startsAt : undefined;
}

/** A fresh idempotency key (random UUID; hex bytes where `randomUUID` is unavailable, e.g. plain-HTTP previews). */
export function newIdempotencyKey(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  return Array.from(crypto.getRandomValues(new Uint8Array(16)), (b: number) =>
    b.toString(16).padStart(2, '0'),
  ).join('');
}
