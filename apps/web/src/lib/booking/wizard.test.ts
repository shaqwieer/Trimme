import { describe, expect, it } from 'vitest';
import { safeReturnTo, withReturnTo } from '@/lib/auth/paths';
import { changeDeadline, cutoffParts, endOf, timeUntil } from './format';
import {
  ANY_PROFESSIONAL,
  currentStep,
  eligibleProfessionals,
  keyFor,
  readWizardQuery,
  resolveStart,
  selectionFrom,
  wizardNotice,
  wizardPath,
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
const OFFERS = [HAIRCUT, PACKAGE];
const PROS = [{ id: 'omar' }, { id: 'majed' }, { id: 'sultan' }];

const parse = (search: string) => {
  const query = readWizardQuery(new URLSearchParams(search));
  const selection = selectionFrom(query, OFFERS, PROS);
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

  it('keeps a professional preselected from their page, and asks for the service first', () => {
    const { selection, step } = parse('pro=omar');
    expect(step).toBe('service');
    // The professional only becomes valid once an offer they do is chosen.
    expect(selection.pro).toBe(ANY_PROFESSIONAL);
    expect(parse(`pro=omar&service=${HAIRCUT.id}`).selection.pro).toBe('omar');
  });

  it('opens a time chip link (pro, service, date and local time) on the review step', () => {
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
    expect(parse(`service=${HAIRCUT.id}&date=2026-10-01&step=review`).step).toBe('time');
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
        readWizardQuery(new URLSearchParams(wizardPath('x', selection, 'time', 'conflict').split('?')[1])),
      ),
    ).toBe('conflict');
    expect(wizardNotice(readWizardQuery(new URLSearchParams('notice=<script>')))).toBeNull();
    // "any" is the default, so it is not written.
    expect(wizardPath('x', { offer: HAIRCUT, pro: ANY_PROFESSIONAL }, 'date')).not.toContain('pro=');
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

describe('idempotency key per booking request (R-BKG-05)', () => {
  const intent = { offerId: HAIRCUT.id, pro: 'omar', startsAt: '2026-10-01T07:00:00Z', note: '' };
  let counter = 0;
  const generate = () => `key-${++counter}`;

  it('reuses the key while the request is the same (double click, retry after a network error)', () => {
    const first = keyFor(undefined, intent, generate);
    expect(keyFor(first, { ...intent }, generate).key).toBe(first.key);
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
});
