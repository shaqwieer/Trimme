import { describe, expect, it } from 'vitest';
import { safeReturnTo, withReturnTo } from '@/lib/auth/paths';
import { calendarDaysUntil, changeDeadline, cutoffParts, endOf, timeUntil } from './format';
import {
  ANY_PROFESSIONAL,
  blockingItem,
  combineOffers,
  durationFor,
  currentStep,
  eligibleProfessionals,
  keyFor,
  nextStep,
  offerKey,
  offersFor,
  readWizardQuery,
  resolveStart,
  restoreSelection,
  rewardFit,
  selectionFrom,
  toggleItem,
  wizardNotice,
  wizardPath,
  withItems,
  type WizardOffer,
} from './wizard';

const HAIRCUT: WizardOffer = {
  kind: 'service',
  id: '0199a0de-5a10-7000-8000-000000000411',
  nameAr: 'قص وتصفيف',
  nameEn: 'Cut and style',
  descriptionAr: null,
  descriptionEn: null,
  price: 60,
  currency: 'SAR',
  durationMinutes: 30,
  professionalIds: ['omar', 'majed'],
};
const PACKAGE: WizardOffer = { ...HAIRCUT, kind: 'package', id: 'pkg-1', professionalIds: ['omar'] };
const BEARD: WizardOffer = {
  ...HAIRCUT,
  id: 'svc-beard',
  nameAr: 'لحية',
  nameEn: 'Beard',
  price: 40,
  durationMinutes: 20,
  professionalIds: ['majed', 'sultan'],
};
const OFFERS = [HAIRCUT, PACKAGE, BEARD];
const PROS = [{ id: 'omar' }, { id: 'majed' }, { id: 'sultan' }];

const parse = (search: string, offers = OFFERS) => {
  const query = readWizardQuery(new URLSearchParams(search));
  const selection = selectionFrom(query, offers, PROS);
  return { query, selection, step: currentStep(query, selection) };
};

describe('booking wizard URL state (D-028, D-096)', () => {
  it('opens a bare link on the service step', () => {
    expect(parse('').step).toBe('service');
  });

  it('opens a service link from the shop page on the professional step with "any" preselected', () => {
    const { selection, step } = parse(`service=${HAIRCUT.id}`);
    expect(step).toBe('professional');
    expect(selection.pro).toBe(ANY_PROFESSIONAL);
  });

  it('keeps a professional preselected from their page or QR code, and asks for the service first (D-133)', () => {
    const { selection, step } = parse('pro=omar');
    expect(step).toBe('service');
    expect(selection.pro).toBe('omar');
    expect(wizardPath('x', selection, 'service')).toBe('/shops/x/book?pro=omar&step=service');
    // Choosing nothing yet, or clearing the choice, keeps them; an unknown professional is "any".
    expect(withItems(selection, [], PROS).pro).toBe('omar');
    expect(parse('pro=nobody').selection.pro).toBe(ANY_PROFESSIONAL);
  });

  it('with the professional chosen, goes from the services straight to the day and time (D-133)', () => {
    expect(parse(`pro=omar&service=${HAIRCUT.id}`).step).toBe('date');
    expect(nextStep('service', 'omar')).toBe('date');
    expect(nextStep('service')).toBe('professional');
    expect(nextStep('professional', 'omar')).toBe('date');
    // Only their services are offered; "any" sees the whole shop.
    expect(offersFor(OFFERS, 'sultan')).toEqual([BEARD]);
    expect(offersFor(OFFERS, ANY_PROFESSIONAL)).toEqual(OFFERS);
  });

  it('opens a full link (pro, service, date and local time, as the sign-in round trip keeps it) on the review step', () => {
    const { selection, step } = parse(`pro=omar&service=${HAIRCUT.id}&date=2026-10-01&time=17:30`);
    expect(step).toBe('review');
    expect(selection).toMatchObject({ pro: 'omar', date: '2026-10-01', time: '17:30' });
  });

  it('falls back to "any" for a professional who does not do the offer, and drops malformed dates and times', () => {
    expect(parse(`pro=sultan&service=${HAIRCUT.id}`).selection.pro).toBe(ANY_PROFESSIONAL);
    expect(parse(`package=pkg-1&pro=majed`).selection.pro).toBe(ANY_PROFESSIONAL);
    const bad = parse(`service=${HAIRCUT.id}&date=01-10-2026&time=25:00`);
    expect(bad.selection.date).toBeUndefined();
    expect(bad.selection.time).toBeUndefined();
    expect(parse('service=unknown&step=review').step).toBe('service');
  });

  it('honours an explicit step only up to the first missing choice', () => {
    expect(parse(`service=${HAIRCUT.id}&step=review`).step).toBe('date');
    // The day and its times are one step (D-129); an old step=time link opens it too.
    expect(parse(`service=${HAIRCUT.id}&date=2026-10-01&step=review`).step).toBe('date');
    expect(parse(`service=${HAIRCUT.id}&date=2026-10-01&time=10:00&step=time`).step).toBe('date');
    expect(parse(`service=${HAIRCUT.id}&date=2026-10-01&time=10:00&step=professional`).step).toBe(
      'professional',
    );
  });

  it('lists only the professionals assigned to the chosen service or package', () => {
    expect(eligibleProfessionals(HAIRCUT, PROS).map((p) => p.id)).toEqual(['omar', 'majed']);
    expect(eligibleProfessionals(PACKAGE, PROS).map((p) => p.id)).toEqual(['omar']);
    expect(eligibleProfessionals(undefined, PROS)).toEqual([]);
  });

  it('round-trips a selection through the URL, and a notice only when given', () => {
    const selection = parse(`pro=omar&service=${HAIRCUT.id}&date=2026-10-01&time=17:30`).selection;
    const path = wizardPath('barber-house', selection, 'review');
    expect(path).toBe(
      `/shops/barber-house/book?service=${HAIRCUT.id}&pro=omar&date=2026-10-01&time=17%3A30&step=review`,
    );
    expect(parse(path.split('?')[1]!).selection).toEqual(selection);
    expect(
      wizardNotice(
        readWizardQuery(new URLSearchParams(wizardPath('x', selection, 'date', 'conflict').split('?')[1])),
      ),
    ).toBe('conflict');
    expect(wizardNotice(readWizardQuery(new URLSearchParams('notice=<script>')))).toBeNull();
    // "any" is the default, so it is not written.
    expect(
      wizardPath('x', { items: [HAIRCUT], offer: HAIRCUT, pro: ANY_PROFESSIONAL }, 'date'),
    ).not.toContain('pro=');
  });

  it('survives the sign-in round trip: sign-in → verify → complete profile keep the whole wizard URL', () => {
    const selection = parse(`pro=omar&package=pkg-1&date=2026-10-01&time=17:30`).selection;
    const wizard = wizardPath('a-very-long-shop-slug-with-many-words-in-it', selection, 'review');
    // Each auth page reads `returnTo` with safeReturnTo and passes it on unchanged.
    const signIn = withReturnTo('/auth/sign-in', wizard);
    const kept = safeReturnTo(new URL(`https://x${signIn}`).searchParams.get('returnTo'), '/account');
    expect(kept).toBe(wizard);
    const verify = withReturnTo('/auth/verify', kept);
    const afterVerify = safeReturnTo(new URL(`https://x${verify}`).searchParams.get('returnTo'), '/account');
    const complete = withReturnTo('/auth/complete-profile', afterVerify);
    expect(safeReturnTo(new URL(`https://x${complete}`).searchParams.get('returnTo'), '/account')).toBe(
      wizard,
    );
    expect(wizard.length).toBeLessThan(512);
  });

  it('resolves the local time from the slots of the day', () => {
    const slots = [
      { localTime: '10:00', startsAt: '2026-10-01T07:00:00Z' },
      { localTime: '10:30', startsAt: '2026-10-01T07:30:00Z' },
    ];
    expect(resolveStart(slots, '10:30')).toBe('2026-10-01T07:30:00Z');
    expect(resolveStart(slots, '11:00')).toBeUndefined();
    expect(resolveStart(undefined, '10:30')).toBeUndefined();
  });
});

describe('several services in one booking (D-125)', () => {
  it('reads repeated services in order, without repeats, and a package first with other services (D-148)', () => {
    const { selection, step } = parse(`service=${HAIRCUT.id}&service=${BEARD.id}&service=${HAIRCUT.id}`);
    expect(selection.items.map((item) => item.id)).toEqual([HAIRCUT.id, BEARD.id]);
    expect(step).toBe('professional');
    expect(parse(`package=pkg-1&service=${HAIRCUT.id}`).selection.items).toEqual([PACKAGE, HAIRCUT]);
  });

  it('adds up durations and prices, joins the names, and keeps only professionals who do every service', () => {
    const combined = combineOffers([HAIRCUT, BEARD])!;
    expect(combined).toMatchObject({
      kind: 'service',
      id: HAIRCUT.id,
      nameAr: 'قص وتصفيف + لحية',
      nameEn: 'Cut and style + Beard',
      price: 100,
      durationMinutes: 50,
      professionalIds: ['majed'],
    });
    expect(combineOffers([HAIRCUT])).toBe(HAIRCUT);
    expect(combineOffers([])).toBeUndefined();
    expect(parse(`service=${HAIRCUT.id}&service=${BEARD.id}&pro=omar`).selection.pro).toBe(ANY_PROFESSIONAL);
  });

  it('books a package with other services; refuses a service already inside it; names the service no barber can join (D-148)', () => {
    const pkg = { ...OFFERS.find((o) => o.kind === 'package')!, itemIds: [HAIRCUT.id] };
    // Choosing the package keeps the other services and takes off the one it already holds.
    const withPackage = toggleItem([HAIRCUT, BEARD], pkg);
    expect(withPackage.items.map((i) => i.id)).toEqual([pkg.id, BEARD.id]);
    expect(withPackage.dropped.map((i) => i.id)).toEqual([HAIRCUT.id]);
    // A service inside the chosen package is refused, and the package stays.
    const refused = toggleItem(withPackage.items, HAIRCUT);
    expect(refused.inPackage?.id).toBe(pkg.id);
    expect(refused.items).toBe(withPackage.items);
    // Un-choosing the package keeps the services.
    expect(toggleItem(withPackage.items, pkg).items.map((i) => i.id)).toEqual([BEARD.id]);
    // The URL's package and services come back together, the package first.
    const parsed = parse(`package=${pkg.id}&service=${BEARD.id}&service=${HAIRCUT.id}`, [
      ...OFFERS.filter((o) => o.kind !== 'package'),
      pkg,
    ]);
    expect(parsed.selection.items.map((i) => i.id)).toEqual([pkg.id, BEARD.id]);

    const lonely = { ...BEARD, id: 'dye', nameAr: 'صبغة', professionalIds: ['nobody'] };
    expect(blockingItem([HAIRCUT, lonely])?.id).toBe('dye');
  });

  it('writes every service into the URL and keys the request on all of them', () => {
    const selection = parse(`service=${HAIRCUT.id}&service=${BEARD.id}&pro=majed`).selection;
    expect(wizardPath('x', selection, 'date')).toBe(
      `/shops/x/book?service=${HAIRCUT.id}&service=${BEARD.id}&pro=majed&step=date`,
    );
    expect(offerKey(selection.items)).not.toBe(offerKey([HAIRCUT]));
  });

  it('keeps the date and time when the services change, and the professional while they still do them all', () => {
    const selection = parse(`service=${HAIRCUT.id}&pro=majed&date=2026-10-01&time=10:00`).selection;
    expect(withItems(selection, [HAIRCUT, BEARD], PROS)).toMatchObject({
      pro: 'majed',
      date: '2026-10-01',
      time: '10:00',
    });
    expect(withItems(parse(`service=${HAIRCUT.id}&pro=omar`).selection, [HAIRCUT, BEARD], PROS).pro).toBe(
      ANY_PROFESSIONAL,
    );
    expect(withItems(selection, [], PROS).date).toBeUndefined();
  });

  it('restores the later choices an older history entry lacks, only for the same services', () => {
    const latest = parse(`service=${HAIRCUT.id}&pro=majed&date=2026-10-01&time=10:00`).selection;
    const older = parse(`service=${HAIRCUT.id}`).selection;
    expect(restoreSelection(older, latest)).toEqual(latest);
    expect(restoreSelection(parse('').selection, latest)).toEqual(latest);
    const other = parse(`service=${BEARD.id}`).selection;
    expect(restoreSelection(other, latest)).toBe(other);
  });
});

describe('idempotency key per booking request (R-BKG-05)', () => {
  const intent = { offerId: HAIRCUT.id, pro: 'omar', startsAt: '2026-10-01T07:00:00Z', note: '' };
  let counter = 0;
  const generate = () => `key-${++counter}`;

  it('reuses the key while the request is the same (double click, retry after a network error)', () => {
    const first = keyFor(undefined, intent, generate);
    expect(keyFor(first, { ...intent }, generate).key).toBe(first.key);
  });

  it('uses a new key when the chosen loyalty coupon changes (D-132)', () => {
    const first = keyFor(undefined, intent, generate);
    expect(keyFor(first, { ...intent, rewardId: '' }, generate).key).toBe(first.key);
    expect(keyFor(first, { ...intent, rewardId: 'reward-1' }, generate).key).not.toBe(first.key);
  });

  it('uses a new key when the offer, professional, time or note changes', () => {
    const first = keyFor(undefined, intent, generate);
    const keys = new Set([
      first.key,
      keyFor(first, { ...intent, offerId: 'pkg-1' }, generate).key,
      keyFor(first, { ...intent, pro: 'any' }, generate).key,
      keyFor(first, { ...intent, startsAt: '2026-10-01T07:30:00Z' }, generate).key,
      keyFor(first, { ...intent, note: 'تدريج' }, generate).key,
    ]);
    expect(keys.size).toBe(5);
  });
});

describe('booking display helpers', () => {
  it('computes the end, the change deadline and the cutoff unit', () => {
    expect(endOf('2026-10-01T07:00:00.000Z', 50)).toBe('2026-10-01T07:50:00.000Z');
    expect(changeDeadline('2026-10-01T07:00:00.000Z', 120)).toBe('2026-10-01T05:00:00.000Z');
    expect(cutoffParts(120)).toEqual({ unit: 'hours', count: 2 });
    expect(cutoffParts(90)).toEqual({ unit: 'minutes', count: 90 });
    expect(cutoffParts(30)).toEqual({ unit: 'minutes', count: 30 });
  });

  it('counts down in days, hours and minutes, never below zero', () => {
    const now = new Date('2026-10-01T07:00:00Z');
    expect(timeUntil('2026-10-03T10:05:00Z', now)).toEqual({ days: 2, hours: 3, minutes: 5 });
    expect(timeUntil('2026-09-30T07:00:00Z', now)).toEqual({ days: 0, hours: 0, minutes: 0 });
  });

  it('counts countdown days by the calendar in the shop time zone, not in 24-hour blocks', () => {
    // Wednesday 30 September, 8:30 PM in Riyadh.
    const now = new Date('2026-09-30T17:30:00Z');
    // Friday 5:00 PM is 44½ hours away: two calendar days, where the 24-hour count said one ("tomorrow").
    expect(timeUntil('2026-10-02T14:00:00Z', now).days).toBe(1);
    expect(calendarDaysUntil('2026-10-02T14:00:00Z', 'Asia/Riyadh', now)).toBe(2);
    // Thursday 11:59 PM is tomorrow; Friday 12:00 AM (Thursday 21:00 UTC) is already the day after.
    expect(calendarDaysUntil('2026-10-01T20:59:00Z', 'Asia/Riyadh', now)).toBe(1);
    expect(calendarDaysUntil('2026-10-01T21:00:00Z', 'Asia/Riyadh', now)).toBe(2);
    // The same instant is the same day in UTC but not in Riyadh.
    expect(calendarDaysUntil('2026-10-01T21:30:00Z', 'UTC', now)).toBe(1);
    expect(calendarDaysUntil('2026-09-29T10:00:00Z', 'Asia/Riyadh', now)).toBe(0);
  });
});

describe('durationFor (D-149)', () => {
  const cut: WizardOffer = {
    kind: 'service',
    id: 'cut',
    nameAr: 'حلاقة',
    nameEn: null,
    descriptionAr: null,
    descriptionEn: null,
    price: 60,
    currency: 'SAR',
    durationMinutes: 30,
    professionalIds: ['ahmed', 'mohamed'],
    professionalDurations: [{ professionalId: 'mohamed', durationMinutes: 45 }],
  };
  const beard: WizardOffer = { ...cut, id: 'beard', durationMinutes: 20, professionalDurations: [] };
  const pkg: WizardOffer = { ...cut, kind: 'package', id: 'pkg', durationMinutes: 40 };

  it("adds each service's length with the chosen barber: their own where set, else the standard", () => {
    expect(durationFor([cut, beard], 'ahmed')).toBe(50);
    expect(durationFor([cut, beard], 'mohamed')).toBe(65);
  });

  it('uses the standard lengths for «أي مختص», and a package keeps the length the owner set', () => {
    expect(durationFor([cut], ANY_PROFESSIONAL)).toBe(30);
    expect(durationFor([pkg, beard], 'mohamed')).toBe(60);
  });
});

describe('rewardFit (D-149)', () => {
  const amountOff = { rewardType: 'AmountOff' as const, rewardServiceId: null, rewardAmount: 20 };
  const freeBeard = { rewardType: 'FreeService' as const, rewardServiceId: 'beard', rewardAmount: null };
  const hair = { kind: 'service' as const, id: 'hair', price: 60 };
  const beard = { kind: 'service' as const, id: 'beard', price: 25 };

  it('takes an amount off, never more than the price', () => {
    expect(rewardFit([hair], 60, amountOff)).toEqual({ usable: true, discount: 20 });
    expect(rewardFit([{ ...hair, price: 15 }], 15, amountOff)).toEqual({ usable: true, discount: 15 });
  });

  it('makes a service free only when the booking includes it', () => {
    expect(rewardFit([hair, beard], 85, freeBeard)).toEqual({ usable: true, discount: 25 });
    expect(rewardFit([hair], 60, freeBeard)).toEqual({ usable: false, reason: 'service' });
  });

  it('never goes with a package', () => {
    const pkg = { kind: 'package' as const, id: 'pkg', price: 110 };
    expect(rewardFit([pkg], 110, amountOff)).toEqual({ usable: false, reason: 'package' });
    expect(rewardFit([pkg, beard], 135, freeBeard)).toEqual({ usable: false, reason: 'package' });
  });
});
