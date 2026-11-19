'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocale, useTranslations } from 'next-intl';
import { type ReactNode, useRef, useState } from 'react';
import { useLoyaltyRewardName } from '@/components/loyalty/LoyaltyParts';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { isValidSaudiMobile, toE164 } from '@/components/ui/digits';
import { PhoneField, TextareaField, TextField } from '@/components/ui/inputs';
import { CheckboxCard, RadioCard } from '@/components/ui/selection';
import { EmptyState, InlineAlert, SkeletonList } from '@/components/ui/states';
import { browserApi } from '@/lib/api/client';
import { ensureOk, useApiErrorMessage } from '@/lib/api/errors';
import { ApiError } from '@/lib/api/problem';
import type { components } from '@/lib/api/schema';
import { endOf } from '@/lib/booking/format';
import { keyFor, type KeyedIntent, newIdempotencyKey, rewardFit } from '@/lib/booking/wizard';
import { type AppLocale, formatDurationMinutes, formatPrice, formatTime } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { BOARD_KEY } from './boardKey';

export type WalkInOffer = {
  kind: 'service' | 'package';
  id: string;
  nameAr: string;
  nameEn: string | null;
  price: number;
  currency: string;
  durationMinutes: number;
  /** A package's services (D-148: a package goes with other services, never with one it already holds). */
  itemIds?: string[];
};

type Kind = 'WalkIn' | 'Phone';

/** What the barber's lookup says about the typed number (D-149): a reward ready and what it is, nothing else. */
type RewardLookup = components['schemas']['LoyaltyLookupResponse'];

const NOW = 'now';
const NAME_MAX = 120;
const NOTE_MAX = 500;
const MAX_SERVICES = 10;

/**
 * A booking the barber records (s-walkin 2397–2459, D-035, D-088, D-147), numbered 1 to 5 whatever is chosen:
 * (1) «حضوري» — the customer is here, the time is taken at once (now, or the barber's next free start today) — or
 * «بالتلفون», an appointment on another day or time; (2) several services, added up, or one package; (3) the barber
 * themselves or a free colleague who does all of it; (4) the time; (5) the customer's name (required), mobile (optional)
 * and a note. The mobile is stored encrypted; the barbers see it on the booking until it is closed, the owner never
 * (D-147). On a phone the summary follows the form; it stays in view beside it from `lg` up.
 */
export function WalkInFlow({
  offers,
  today,
  timeZone,
  initialProfessionalId,
  initialStart,
  initialDate,
  initialKind,
  ownProfessionalId,
}: {
  offers: WalkInOffer[];
  today: string;
  timeZone: string;
  /** The barber's own professional, marked «أنت» among the colleagues. */
  ownProfessionalId?: string;
  initialProfessionalId?: string;
  initialStart?: string;
  initialDate?: string;
  /** «حجز بالتلفون» from the day screen's quick buttons (D-151) opens with the phone kind chosen. */
  initialKind?: Kind;
}) {
  const t = useTranslations('shopBoard.walkIn');
  const tCatalog = useTranslations('booking.catalog');
  const tv = useTranslations('validation');
  const locale = useLocale() as AppLocale;
  const apiMessage = useApiErrorMessage();
  const rewardName = useLoyaltyRewardName();
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<Kind>(
    initialKind ?? (initialDate && initialDate !== today ? 'Phone' : 'WalkIn'),
  );
  const [serviceIds, setServiceIds] = useState<string[]>(() => {
    const first = offers.find((o) => o.kind === 'service');
    return first ? [first.id] : [];
  });
  const [packageId, setPackageId] = useState<string | null>(() =>
    offers.some((o) => o.kind === 'service') ? null : (offers[0]?.id ?? null),
  );
  const [date, setDate] = useState(initialDate ?? today);
  const [professionalId, setProfessionalId] = useState(initialProfessionalId ?? '');
  const [start, setStart] = useState(initialStart ?? '');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [phoneTouched, setPhoneTouched] = useState(false);
  // The typed number's loyalty reward (D-149), looked up once the number is complete; dropped when it changes.
  const [lookup, setLookup] = useState<{ phone: string; result: RewardLookup } | null>(null);
  const [useReward, setUseReward] = useState(false);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const [offerNote, setOfferNote] = useState<string>();
  const [created, setCreated] = useState<{ id: string; reference: string; kind: Kind } | null>(null);
  const keyed = useRef<KeyedIntent | undefined>(undefined);

  const services = offers.filter((o) => o.kind === 'service');
  const packages = offers.filter((o) => o.kind === 'package');
  // A package and other services together (D-148): the package first, then the services it does not hold.
  const chosenPackage = packages.find((p) => p.id === packageId);
  const chosen: WalkInOffer[] = [
    ...(chosenPackage ? [chosenPackage] : []),
    ...serviceIds.map((id) => services.find((s) => s.id === id)).filter((s): s is WalkInOffer => Boolean(s)),
  ];
  // The standard lengths; once a barber is chosen, theirs (D-149: a barber's own length per service, from the API).
  const standard = chosen.reduce((sum, o) => sum + o.durationMinutes, 0);
  const price = chosen.reduce((sum, o) => sum + o.price, 0);
  const currency = chosen[0]?.currency ?? 'SAR';
  const offerKey = `package:${packageId ?? ''}|services:${serviceIds.join(',')}`;
  const day = kind === 'WalkIn' ? today : date;

  const options = useQuery({
    queryKey: [BOARD_KEY, 'walk-in', offerKey, day],
    enabled: chosen.length > 0,
    queryFn: async () =>
      ensureOk(
        await browserApi.GET('/api/v1/shop/availability/walk-in', {
          params: {
            query: {
              serviceIds: serviceIds.length > 0 ? serviceIds : undefined,
              packageId: packageId ?? undefined,
              date: day,
            },
          },
        }),
      ),
  });

  const professional = options.data?.professionals.find((p) => p.id === professionalId);
  const duration = professional?.durationMinutes ?? standard;
  const startsAt = start === NOW ? options.data?.now : start || undefined;
  const nameError =
    name.trim().length === 0 ? undefined : name.trim().length > NAME_MAX ? t('nameTooLong') : undefined;
  const phoneValid = phone === '' || isValidSaudiMobile(phone);
  // A reward the typed number has ready (D-149): never with a package; a free service once it is chosen.
  const readyReward = lookup && lookup.phone === phone && lookup.result.rewardReady ? lookup.result : null;
  const rewardTerms = readyReward && {
    rewardType: readyReward.rewardType ?? 'AmountOff',
    rewardServiceId: readyReward.rewardServiceId,
    rewardServiceNameAr: readyReward.titleAr,
    rewardServiceNameEn: readyReward.titleEn,
    rewardAmount: readyReward.rewardAmount,
  };
  const fit = rewardTerms ? rewardFit(chosen, price, rewardTerms) : null;
  const discount = useReward && fit?.usable ? fit.discount : undefined;
  const phoneError = phoneTouched && !phoneValid ? tv('phoneInvalid') : undefined;
  const ready = Boolean(
    chosen.length > 0 && professional && startsAt && name.trim() && !nameError && phoneValid,
  );

  if (offers.length === 0) {
    return <EmptyState icon="tag" title={t('noServices')} />;
  }

  const resetTime = () => {
    setStart('');
  };
  const offerName = (o: WalkInOffer) => localizedName(locale, o.nameAr, o.nameEn);
  const toggleService = (id: string) => {
    const service = services.find((s) => s.id === id);
    if (chosenPackage?.itemIds?.includes(id) && service) {
      // Already in the chosen package: say so rather than add it twice.
      setOfferNote(tCatalog('inPackage', { service: offerName(service), package: offerName(chosenPackage) }));
      return;
    }
    setOfferNote(undefined);
    setServiceIds((current) =>
      current.includes(id)
        ? current.filter((s) => s !== id)
        : current.length >= MAX_SERVICES
          ? current
          : [...current, id],
    );
    resetTime();
  };
  const togglePackage = (id: string) => {
    if (packageId === id) {
      setPackageId(null);
      setOfferNote(undefined);
      resetTime();
      return;
    }
    const pkg = packages.find((p) => p.id === id);
    const dropped = services.filter((s) => serviceIds.includes(s.id) && pkg?.itemIds?.includes(s.id));
    setPackageId(id);
    setServiceIds((current) => current.filter((s) => !dropped.some((d) => d.id === s)));
    setOfferNote(
      pkg && dropped.length > 0
        ? tCatalog('dropped', {
            services: dropped.map(offerName).join(locale === 'ar' ? '، ' : ', '),
            package: offerName(pkg),
          })
        : undefined,
    );
    resetTime();
  };

  const checkReward = async (national: string) => {
    const e164 = toE164(national);
    if (!e164) return;
    try {
      const { data } = await browserApi.POST('/api/v1/shop/loyalty/lookup', { body: { phone: e164 } });
      if (data) setLookup({ phone: national, result: data });
    } catch {
      // No lookup, no reward offered: the booking goes ahead without one.
    }
  };

  const submit = async () => {
    if (chosen.length === 0 || !professional || !startsAt) return;
    setError(undefined);
    setSubmitting(true);
    const body = {
      serviceId: null,
      serviceIds,
      packageId,
      professionalId: professional.id,
      startsAt: start === NOW ? null : start,
      customerName: name.trim(),
      customerPhone: phone ? toE164(phone) : null,
      note: note.trim() || null,
      channel: kind,
      useLoyaltyReward: discount !== undefined,
    };
    keyed.current = keyFor(
      keyed.current,
      {
        offerId: offerKey,
        pro: professional.id,
        startsAt: `${kind}|${start}`,
        note: `${name.trim()}|${phone}|${note.trim()}|${discount !== undefined}`,
      },
      newIdempotencyKey,
    );
    try {
      const booking = ensureOk(
        await browserApi.POST('/api/v1/shop/bookings/walk-in', {
          params: { header: { 'Idempotency-Key': keyed.current.key } },
          body,
        }),
      );
      setCreated({ id: booking.id, reference: booking.reference, kind });
      setPhone('');
      setPhoneTouched(false);
      setLookup(null);
      setUseReward(false);
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
        <h2 className="text-h3 font-bold text-navy-900">
          {created.kind === 'Phone' ? t('done.phoneTitle') : t('done.title')}
        </h2>
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

  // Every step renders, so the numbers in the copy always read 1 to 5 (the «١، ٢، ٤» bug: step 3 used to appear only
  // after a barber was picked).
  const fieldset = (label: string, children: ReactNode, testId?: string) => (
    <fieldset className="flex min-w-0 flex-col gap-2.5" data-testid={testId}>
      <legend className="pb-2 text-label font-bold text-text-strong">{label}</legend>
      {children}
    </fieldset>
  );

  return (
    <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[1.6fr_1fr]" data-testid="walk-in">
      <div className="flex flex-col gap-6 rounded-card border border-border bg-surface p-5 shadow-e1">
        <p className="text-helper text-text-secondary">{t('intro')}</p>

        {fieldset(
          t('steps.kind'),
          <div className="grid gap-2.5 sm:grid-cols-2">
            {(['WalkIn', 'Phone'] as const).map((k) => (
              <RadioCard
                key={k}
                name="walk-in-kind"
                value={k}
                checked={kind === k}
                onChange={() => {
                  setKind(k);
                  setStart('');
                  if (k === 'WalkIn') setDate(today);
                }}
              >
                <span className="block text-label font-bold text-text-primary">{t(`kinds.${k}.title`)}</span>
                <span className="block text-helper text-text-secondary">{t(`kinds.${k}.body`)}</span>
              </RadioCard>
            ))}
          </div>,
          'walk-in-kind',
        )}

        {fieldset(
          t('steps.service'),
          <>
            {services.length > 0 && (
              <div className="grid gap-2.5 sm:grid-cols-2">
                {services.map((o) => (
                  <CheckboxCard
                    key={o.id}
                    name="walk-in-service"
                    value={o.id}
                    checked={serviceIds.includes(o.id)}
                    onChange={() => toggleService(o.id)}
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
                  </CheckboxCard>
                ))}
              </div>
            )}
            {packages.length > 0 && (
              <>
                <p className="pt-1 text-helper font-bold text-text-secondary">{t('packages')}</p>
                <div className="grid gap-2.5 sm:grid-cols-2">
                  {packages.map((o) => (
                    <CheckboxCard
                      key={o.id}
                      name="walk-in-package"
                      value={o.id}
                      checked={packageId === o.id}
                      onChange={() => togglePackage(o.id)}
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
                    </CheckboxCard>
                  ))}
                </div>
              </>
            )}
            {offerNote && (
              <p role="status" className="rounded-card bg-warning-50 p-3 text-label text-warning-700">
                {offerNote}
              </p>
            )}
            {chosen.length > 0 && (
              <p className="text-label font-bold text-navy-900" data-testid="walk-in-total">
                {t('chosen', {
                  count: chosen.length,
                  duration: formatDurationMinutes(duration, locale),
                  price: formatPrice(price, locale, currency),
                })}
              </p>
            )}
          </>,
          'walk-in-services',
        )}

        {kind === 'Phone' && (
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
        )}

        {fieldset(
          t('steps.professional'),
          chosen.length === 0 ? (
            <p className="text-helper text-text-secondary">{t('pickServiceFirst')}</p>
          ) : options.isPending ? (
            <SkeletonList rows={2} label={t('loading')} />
          ) : options.isError ? (
            <InlineAlert tone="danger" title={t('loadError')} />
          ) : options.data.professionals.length === 0 ? (
            <p className="text-helper text-text-secondary">{t('noProfessionals')}</p>
          ) : (
            <div className="grid gap-2.5 sm:grid-cols-2">
              {options.data.professionals.map((p) => {
                const freeNow = kind === 'WalkIn' && p.freeNow;
                const availability = freeNow
                  ? t('freeNow')
                  : p.nextFreeAt
                    ? t('freeAt', { time: formatTime(p.nextFreeAt, locale, timeZone) })
                    : t('noTimes');
                // A barber whose own length differs from the standard says so, as in the customer's booking.
                const own =
                  p.durationMinutes !== standard ? formatDurationMinutes(p.durationMinutes, locale) : null;
                return (
                  <RadioCard
                    key={p.id}
                    name="walk-in-professional"
                    value={p.id}
                    checked={professionalId === p.id}
                    disabled={!freeNow && !p.nextFreeAt}
                    onChange={() => {
                      setProfessionalId(p.id);
                      setStart(freeNow ? NOW : '');
                    }}
                  >
                    <span className="block text-label font-bold text-text-primary">
                      {localizedName(locale, p.nameAr, p.nameEn)}
                      {p.id === ownProfessionalId && (
                        <span className="ms-1.5 text-helper font-normal text-text-tertiary">{t('you')}</span>
                      )}
                    </span>
                    <span
                      className={`block text-helper ${freeNow ? 'font-bold text-success-700' : 'text-text-secondary'}`}
                    >
                      {availability}
                    </span>
                    {own && (
                      <span
                        className="block text-helper text-text-secondary"
                        data-testid="walk-in-own-length"
                      >
                        {own}
                      </span>
                    )}
                  </RadioCard>
                );
              })}
            </div>
          ),
        )}

        {fieldset(
          t('steps.time'),
          !professional ? (
            <p className="text-helper text-text-secondary">{t('pickProfessionalFirst')}</p>
          ) : (
            <div className="flex flex-wrap gap-2" data-testid="walk-in-times">
              {kind === 'WalkIn' && professional.freeNow && (
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
          ),
        )}

        {fieldset(
          t('steps.customer'),
          <>
            <TextField
              label={t('name')}
              value={name}
              maxLength={NAME_MAX}
              autoComplete="off"
              error={nameError}
              onChange={(e) => setName(e.target.value)}
            />
            <PhoneField
              label={t('phone')}
              optional
              autoComplete="off"
              value={phone}
              error={phoneError}
              onValueChange={(national) => {
                setPhone(national);
                setUseReward(false);
                if (isValidSaudiMobile(national)) void checkReward(national);
              }}
              onBlur={() => setPhoneTouched(true)}
            />
            {rewardTerms && fit && (
              <div
                className="flex flex-col gap-2 rounded-card border border-success-500/40 bg-success-50 p-3"
                data-testid="walk-in-reward"
              >
                <p className="flex items-center gap-1.5 text-label font-bold text-success-700">
                  <Icon name="tag" className="size-4 shrink-0" />
                  {t('loyalty.ready', { reward: rewardName(rewardTerms) })}
                </p>
                <CheckboxCard
                  checked={discount !== undefined}
                  disabled={!fit.usable}
                  onChange={(e) => setUseReward(e.target.checked)}
                >
                  <span className="flex flex-col gap-0.5">
                    <span>{t('loyalty.use')}</span>
                    {!fit.usable && (
                      <span className="text-helper text-text-secondary">
                        {fit.reason === 'package'
                          ? t('loyalty.notWithPackage')
                          : t('loyalty.needsService', {
                              service: localizedName(
                                locale,
                                rewardTerms.rewardServiceNameAr ?? '',
                                rewardTerms.rewardServiceNameEn,
                              ),
                            })}
                      </span>
                    )}
                  </span>
                </CheckboxCard>
                <p className="text-helper text-text-secondary">{t('loyalty.hint')}</p>
              </div>
            )}
            <TextareaField
              label={t('note')}
              optional
              placeholder={t('notePlaceholder')}
              maxLength={NOTE_MAX}
              rows={2}
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
          </>,
        )}
      </div>

      <aside
        className="flex flex-col gap-3 rounded-card border border-border bg-surface p-5 shadow-e1 lg:sticky lg:top-24"
        aria-labelledby="walk-in-summary"
      >
        <h2 id="walk-in-summary" className="text-h3 font-bold text-navy-900">
          {t('summary.title')}
        </h2>
        <dl className="flex flex-col divide-y divide-border-row text-label">
          {[
            [t('summary.kind'), t(`kinds.${kind}.title`)],
            [
              t('summary.service'),
              chosen.length > 0
                ? chosen.map((o) => localizedName(locale, o.nameAr, o.nameEn)).join(' + ')
                : '—',
            ],
            [
              t('summary.professional'),
              professional ? localizedName(locale, professional.nameAr, professional.nameEn) : '—',
            ],
            [t('summary.starts'), startsAt ? formatTime(startsAt, locale, timeZone) : '—'],
            [
              t('summary.ends'),
              startsAt && duration > 0 ? formatTime(endOf(startsAt, duration), locale, timeZone) : '—',
            ],
            [t('summary.total'), chosen.length > 0 ? formatPrice(price, locale, currency) : '—'],
            ...(discount !== undefined
              ? [
                  [t('loyalty.discount'), `−${formatPrice(discount, locale, currency)}`],
                  [t('loyalty.due'), formatPrice(price - discount, locale, currency)],
                ]
              : []),
          ].map(([label, value]) => (
            <div key={label} className="flex items-center justify-between gap-3 py-2">
              <dt className="text-text-secondary">{label}</dt>
              <dd className="text-end font-bold text-text-primary">{value}</dd>
            </div>
          ))}
        </dl>
        {error && <InlineAlert tone="danger" title={error} />}
        <Button size="lg" fullWidth disabled={!ready} loading={submitting} onClick={submit}>
          {kind === 'Phone' ? t('submitPhone') : t('submit')}
        </Button>
        <p className="text-helper text-text-tertiary">{t('blocksOnline')}</p>
      </aside>
    </div>
  );
}
