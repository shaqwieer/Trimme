/**
 * Booking wizard state (D-028, D-096, D-125). The URL is the source of truth, so a step survives a reload, the back
 * button and the sign-in round trip: one or more `service` (booked together, back to back) or one `package`, `pro` (a
 * professional id or `any`), `date` (YYYY-MM-DD in the shop's time zone), `time` (local HH:mm) and `step`. A
 * professional's page and QR code link `pro` alone, so the customer picks the service, then the time (D-133).
 * Everything here is pure; the wizard component fetches the data and calls these.
 */

/** The day and its times are one step (D-129): the times show under the days as soon as a day is tapped. */
export const WIZARD_STEPS = ['service', 'professional', 'date', 'review'] as const;
export type WizardStep = (typeof WIZARD_STEPS)[number];

export const ANY_PROFESSIONAL = 'any';

export type WizardQuery = {
  /** Every `service` parameter, in order. */
  services: string[];
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
  /** A service's section in its shop, its tab (D-148, D-149). */
  sectionId?: string | null;
  /** A package's services (D-148: what it saves, and a service chosen twice). */
  itemIds?: string[];
  /** A service's own length for the barbers who set one (D-149); the others take `durationMinutes`. */
  professionalDurations?: { professionalId: string; durationMinutes: number }[];
};

export type WizardProfessional = { id: string };

/** The selections the URL carries after validation against the shop's data. */
export type WizardSelection = {
  /** What is booked: one package and/or services (D-148), the package first. */
  items: WizardOffer[];
  /** The items as one offer (see {@link combineOffers}); undefined when nothing is chosen. */
  offer?: WizardOffer;
  /** A professional id, or {@link ANY_PROFESSIONAL}. */
  pro: string;
  date?: string;
  time?: string;
};

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

type Params = URLSearchParams | Record<string, string | string[] | undefined>;

/** At most this many services in one booking (the API's limit). */
export const MAX_SERVICES = 10;

function read(params: Params, key: string): string | undefined {
  if (params instanceof URLSearchParams) return params.get(key) ?? undefined;
  const value = params[key];
  return (Array.isArray(value) ? value[0] : value) || undefined;
}

function readAll(params: Params, key: string): string[] {
  const values = params instanceof URLSearchParams ? params.getAll(key) : [params[key] ?? []].flat();
  return values.filter(Boolean);
}

export function readWizardQuery(params: Params): WizardQuery {
  return {
    services: readAll(params, 'service'),
    package: read(params, 'package'),
    pro: read(params, 'pro'),
    date: read(params, 'date'),
    time: read(params, 'time'),
    step: read(params, 'step'),
    notice: read(params, 'notice'),
  };
}

/**
 * The professional a selection keeps: one who does every chosen item, or, before anything is chosen, any of the shop's
 * (a link from their page or QR code, D-133). Anyone else becomes "any".
 */
function keptProfessional(
  pro: string | undefined,
  offer: WizardOffer | undefined,
  pros: WizardProfessional[],
): string {
  const candidates = offer ? eligibleProfessionals(offer, pros) : pros;
  return pro && candidates.some((p) => p.id === pro) ? pro : ANY_PROFESSIONAL;
}

/** The offers the service step lists: with a professional already chosen, only theirs (D-133). */
export function offersFor(offers: WizardOffer[], pro: string): WizardOffer[] {
  return pro === ANY_PROFESSIONAL ? offers : offers.filter((o) => o.professionalIds.includes(pro));
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
 * Several services booked together as one offer, as the API books them: one professional does them back to back, so
 * durations and prices add up and only professionals assigned to every service qualify. The first item's kind and id
 * stand for the whole.
 */
export function combineOffers(items: WizardOffer[]): WizardOffer | undefined {
  const [first, ...rest] = items;
  if (!first || rest.length === 0) return first;
  const names = items.map((item) => item.nameEn);
  return {
    ...first,
    nameAr: items.map((item) => item.nameAr).join(' + '),
    nameEn: names.every((name): name is string => Boolean(name)) ? names.join(' + ') : null,
    descriptionAr: null,
    descriptionEn: null,
    price: items.reduce((sum, item) => sum + item.price, 0),
    durationMinutes: items.reduce((sum, item) => sum + item.durationMinutes, 0),
    professionalIds: first.professionalIds.filter((id) =>
      rest.every((item) => item.professionalIds.includes(id)),
    ),
  };
}

/**
 * How long the chosen items take with this professional (D-149): a service takes the barber's own length when they set
 * one, else its standard length; a package keeps the length the owner set. With «أي مختص» the standard lengths are
 * shown (the server applies each barber's own when it finds the time).
 */
export function durationFor(items: WizardOffer[], pro: string): number {
  return items.reduce((sum, item) => {
    const own =
      item.kind === 'service' && pro !== ANY_PROFESSIONAL
        ? item.professionalDurations?.find((d) => d.professionalId === pro)?.durationMinutes
        : undefined;
    return sum + (own ?? item.durationMinutes);
  }, 0);
}

/** A loyalty points reward the customer has ready at this shop (D-149): what it gives. */
export type WizardPointsReward = {
  rewardType: 'FreeService' | 'AmountOff';
  rewardServiceId: string | null;
  rewardServiceNameAr: string | null;
  rewardServiceNameEn: string | null;
  rewardAmount: number | null;
};

/** Whether the reward fits the chosen items and what it takes off, or why it does not fit. */
export type RewardFit = { usable: true; discount: number } | { usable: false; reason: 'package' | 'service' };

/**
 * The server's rule for a points reward on a booking (D-149), shown before sending: never with a package; an amount off
 * never more than the price; a free service only when the booking includes it, taking off that service's price.
 */
export function rewardFit(
  items: readonly { kind: 'service' | 'package'; id: string; price: number }[],
  price: number,
  reward: Pick<WizardPointsReward, 'rewardType' | 'rewardServiceId' | 'rewardAmount'>,
): RewardFit {
  if (items.some((item) => item.kind === 'package')) return { usable: false, reason: 'package' };
  const total = Math.max(price, 0);
  if (reward.rewardType === 'AmountOff') {
    return { usable: true, discount: Math.min(reward.rewardAmount ?? 0, total) };
  }
  const free = items.find((item) => item.kind === 'service' && item.id === reward.rewardServiceId);
  return free
    ? { usable: true, discount: Math.min(free.price, total) }
    : { usable: false, reason: 'service' };
}

/**
 * The selection with these items. The professional stays when they do all of them, or while nothing is chosen (else
 * "any"); the date and time stay too, and the availability answers say whether they still fit (D-125).
 */
export function withItems<P extends WizardProfessional>(
  selection: WizardSelection,
  items: WizardOffer[],
  pros: P[],
): WizardSelection {
  const offer = combineOffers(items);
  const pro = keptProfessional(selection.pro, offer, pros);
  return {
    items,
    offer,
    pro,
    date: offer ? selection.date : undefined,
    time: offer ? selection.time : undefined,
  };
}

/**
 * Keeps only what is valid for this shop: known offers (one package, or services without repeats), a professional who
 * does them all, or any of the shop's before an offer is chosen (else "any"), a well-formed date and time. Whether the
 * date and time are still free is checked against the availability answers later.
 */
export function selectionFrom(
  query: WizardQuery,
  offers: WizardOffer[],
  pros: WizardProfessional[],
): WizardSelection {
  const pkg = query.package ? offers.find((o) => o.kind === 'package' && o.id === query.package) : undefined;
  // A package with other services in one booking (D-148); a service already inside the package is dropped.
  const services = [...new Set(query.services)]
    .map((id) => offers.find((o) => o.kind === 'service' && o.id === id))
    .filter((o): o is WizardOffer => Boolean(o) && !pkg?.itemIds?.includes(o!.id))
    .slice(0, MAX_SERVICES);
  const items = pkg ? [pkg, ...services] : services;
  const offer = combineOffers(items);
  const pro = keptProfessional(query.pro, offer, pros);
  const date = offer && query.date && DATE.test(query.date) ? query.date : undefined;
  const time = date && query.time && TIME.test(query.time) ? query.time : undefined;
  return { items, offer, pro, date, time };
}

/**
 * What choosing (or un-choosing) an item does to the items (D-148, «٤-٢»): one package at a time, together with other
 * services; a service already inside the chosen package is refused with `inPackage` (the package stays), and choosing a
 * package takes off the services it already holds (`dropped`).
 */
export function toggleItem(
  items: WizardOffer[],
  item: WizardOffer,
): { items: WizardOffer[]; inPackage?: WizardOffer; dropped: WizardOffer[] } {
  const chosen = items.some((i) => i.kind === item.kind && i.id === item.id);
  const pkg = items.find((i) => i.kind === 'package');
  const services = items.filter((i) => i.kind === 'service');
  if (item.kind === 'package') {
    if (chosen) return { items: services, dropped: [] };
    const dropped = services.filter((s) => item.itemIds?.includes(s.id));
    return { items: [item, ...services.filter((s) => !dropped.includes(s))], dropped };
  }
  if (chosen) return { items: items.filter((i) => !(i.kind === 'service' && i.id === item.id)), dropped: [] };
  if (pkg?.itemIds?.includes(item.id)) return { items, inPackage: pkg, dropped: [] };
  return { items: [...(pkg ? [pkg] : []), ...[...services, item].slice(0, MAX_SERVICES)], dropped: [] };
}

/**
 * The item that leaves no single barber for the rest (D-148, «٤-٨»): the most recently chosen one whose removal lets
 * one barber do all the others. Undefined when nothing on its own is the cause.
 */
export function blockingItem(items: WizardOffer[]): WizardOffer | undefined {
  for (const candidate of [...items].reverse()) {
    const rest = items.filter((i) => i !== candidate);
    if (rest.length > 0 && (combineOffers(rest)?.professionalIds.length ?? 0) > 0) return candidate;
  }
  return undefined;
}

/** One string for the booked items, so a different set of services is a different choice and request. */
export function offerKey(items: WizardOffer[]): string {
  return items.map((item) => `${item.kind}:${item.id}`).join(',');
}

/**
 * Fills in what an older history entry lacks from the latest choices (D-125). Going back with the browser's or the
 * phone's back button lands on an entry written before the later steps were chosen, and that must not drop them. Only
 * missing values are restored, and only for the same items.
 */
export function restoreSelection(current: WizardSelection, latest: WizardSelection): WizardSelection {
  if (current.items.length > 0 && offerKey(current.items) !== offerKey(latest.items)) return current;
  const items = current.items.length > 0 ? current.items : latest.items;
  const date = current.date ?? latest.date;
  return {
    items,
    offer: combineOffers(items),
    pro: current.pro !== ANY_PROFESSIONAL ? current.pro : latest.pro,
    date,
    time: current.time ?? (date === latest.date ? latest.time : undefined),
  };
}

/** The first step whose choice is missing: nothing after it can be shown yet. */
export function firstIncompleteStep(selection: WizardSelection): WizardStep {
  if (!selection.offer) return 'service';
  if (!selection.date || !selection.time) return 'date';
  return 'review';
}

/**
 * The step to show. An explicit `step` is honoured up to the first incomplete one; without it (links from the shop and
 * professional pages) the wizard opens at the furthest step the link implies: a service goes to the professional step
 * ("any" preselected), or straight to the day-and-time step when the link names the professional (D-133); a service
 * with a date goes to the day-and-time step, a full link to the review. An old `step=time` link opens the day-and-time
 * step.
 */
export function currentStep(query: WizardQuery, selection: WizardSelection): WizardStep {
  const limit = WIZARD_STEPS.indexOf(firstIncompleteStep(selection));
  const asked = WIZARD_STEPS.indexOf((query.step === 'time' ? 'date' : query.step) as WizardStep);
  if (asked >= 0) return WIZARD_STEPS[Math.min(asked, limit)]!;
  if (!selection.offer) return 'service';
  if (!selection.date) return selection.pro === ANY_PROFESSIONAL ? 'professional' : 'date';
  return WIZARD_STEPS[limit]!;
}

export function previousStep(step: WizardStep): WizardStep | undefined {
  const index = WIZARD_STEPS.indexOf(step);
  return index > 0 ? WIZARD_STEPS[index - 1] : undefined;
}

/**
 * The step after this one. With the professional already chosen (their page or QR code, or an earlier pass), the
 * services lead straight to the day and time (D-133); going back still reaches the professional step to change them.
 */
export function nextStep(step: WizardStep, pro: string = ANY_PROFESSIONAL): WizardStep | undefined {
  if (step === 'service' && pro !== ANY_PROFESSIONAL) return 'date';
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
  for (const item of selection.items) params.append(item.kind, item.id);
  if (selection.pro !== ANY_PROFESSIONAL) params.set('pro', selection.pro);
  if (selection.date) params.set('date', selection.date);
  if (selection.time) params.set('time', selection.time);
  params.set('step', step);
  if (notice) params.set('notice', notice);
  return `/shops/${encodeURIComponent(slug)}/book?${params.toString()}`;
}

/**
 * What a booking request is about; a new idempotency key is used whenever any of it changes. For the wizard,
 * `offerId` is {@link offerKey} of every booked item.
 */
/** What a submission asks for; `rewardId` is the loyalty coupon chosen on the review step (D-132), if any. */
export type BookingIntent = {
  offerId: string;
  pro: string;
  startsAt: string;
  note: string;
  rewardId?: string;
};

export type KeyedIntent = { intent: BookingIntent; key: string };

function sameIntent(a: BookingIntent, b: BookingIntent): boolean {
  return (
    a.offerId === b.offerId &&
    a.pro === b.pro &&
    a.startsAt === b.startsAt &&
    a.note === b.note &&
    (a.rewardId ?? '') === (b.rewardId ?? '')
  );
}

/**
 * The idempotency key for a submission (R-BKG-05): the same key while the request is the same (a double click or a
 * retry after a network error replays the first booking), a new one as soon as the offer, professional, time, note or
 * chosen coupon changes (the API refuses a reused key with another body).
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
