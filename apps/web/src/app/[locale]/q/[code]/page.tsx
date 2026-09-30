import type { Metadata } from 'next';
import Image from 'next/image';
import { notFound } from 'next/navigation';
import { cache, Suspense } from 'react';
import { getTranslations } from 'next-intl/server';
import { QrVisitRecorder } from '@/components/qr/QrVisitRecorder';
import { PublicShell } from '@/components/shell/PublicShell';
import { Avatar, ImagePlaceholder } from '@/components/ui/Avatar';
import { ButtonLink } from '@/components/ui/Button';
import { Icon } from '@/components/ui/icons';
import { EmptyState, InlineAlert, Skeleton } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import { asLocale } from '@/i18n/routing';
import { dataOrNull, getPublicApi } from '@/lib/api/public';
import { optional } from '@/lib/api/safe';
import { openingLabel } from '@/lib/discovery/opening';
import {
  bookHref,
  getPublicProfessional,
  getPublicShop,
  getPublicShopStatus,
} from '@/lib/discovery/shop-data';
import {
  type AppLocale,
  formatDate,
  formatDurationMinutes,
  formatPrice,
  formatTime,
} from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';
import { earliestSlots } from '@/lib/qr';
import { NO_INDEX } from '@/lib/seo/site';

/** The scanned code, resolved once per request (shared by the metadata and the page); never cached (D-114). */
const resolveCode = cache(async (code: string) => {
  const api = await getPublicApi();
  return dataOrNull(await api.GET('/api/v1/public/qr/{code}', { params: { path: { code } } }), 'qr code');
});

type Target = NonNullable<Awaited<ReturnType<typeof resolveCode>>>;

/** The page a code stands for: its canonical URL, so the QR landing is never indexed on its own (spec §6). */
function canonicalPath(target: Target): string {
  return target.targetType === 'Professional' && target.professionalSlug
    ? `/shops/${target.shopSlug}/professionals/${target.professionalSlug}`
    : `/shops/${target.shopSlug}`;
}

export async function generateMetadata({ params }: PageProps<'/[locale]/q/[code]'>): Promise<Metadata> {
  const { locale: raw, code } = await params;
  const locale = asLocale(raw);
  const target = await resolveCode(code);
  const shop = target ? await getPublicShop(target.shopSlug) : null;
  if (!target || !shop) return { robots: NO_INDEX };
  const t = await getTranslations({ locale, namespace: 'qrLanding' });
  return {
    title: t('meta.title', { name: localizedName(locale, shop.nameAr, shop.nameEn) }),
    robots: NO_INDEX,
    alternates: { canonical: `/${locale}${canonicalPath(target)}` },
  };
}

/** «أقرب الأوقات اليوم»: live, never cached; each time opens the wizard at that professional, offer, day and time. */
async function EarliestTimes({
  target,
  locale,
  timeZone,
}: {
  target: Target;
  locale: AppLocale;
  timeZone: string;
}) {
  const [t, status] = await Promise.all([
    getTranslations({ locale, namespace: 'qrLanding.times' }),
    getPublicShopStatus(target.shopSlug),
  ]);
  if (!status?.acceptsOnlineBookings) return null;

  const api = await getPublicApi();
  const slug = target.shopSlug;
  const professionals =
    target.targetType === 'Professional' && target.professionalId && target.professionalSlug
      ? [{ id: target.professionalId, slug: target.professionalSlug }]
      : (
          (await optional(
            () => api.GET('/api/v1/public/shops/{slug}/professionals', { params: { path: { slug } } }),
            'professionals',
          )) ?? []
        )
          .slice(0, 8)
          .map((p) => ({ id: p.id, slug: p.slug }));
  const next = await Promise.all(
    professionals.map(async (p) => {
      const result = await optional(
        () =>
          api.GET('/api/v1/public/shops/{slug}/professionals/{professionalSlug}/next-slots', {
            params: { path: { slug, professionalSlug: p.slug } },
          }),
        'next slots',
      );
      return {
        professionalId: p.id,
        date: result?.bookable ? result.date : null,
        slots: result?.slots ?? [],
        offer: result?.offer ?? null,
      };
    }),
  );
  const { date, slots } = earliestSlots(next);
  const today = new Intl.DateTimeFormat('en-CA', { timeZone }).format(new Date());

  return (
    <section aria-labelledby="qr-times" className="flex flex-col gap-2.5" data-testid="qr-times">
      <h2 id="qr-times" className="text-h3 font-bold text-navy-900">
        {!date || date === today
          ? t('today')
          : t('on', { day: formatDate(`${date}T12:00:00Z`, locale, { timeZone: 'UTC' }) })}
      </h2>
      {slots.length === 0 ? (
        <p className="text-helper text-text-secondary">{t('none')}</p>
      ) : (
        <ul className="flex flex-wrap gap-2">
          {slots.map((slot, index) => {
            const time = formatTime(slot.startsAt, locale, timeZone);
            return (
              <li key={slot.startsAt}>
                <Link
                  href={bookHref(slug, {
                    pro: slot.professionalId,
                    [slot.isPackage ? 'package' : 'service']: slot.offerId,
                    date: date ?? undefined,
                    time: slot.localTime,
                  })}
                  aria-label={t('slot', { time })}
                  className={
                    index === 0
                      ? 'inline-flex min-h-11 items-center rounded-field bg-navy-900 px-4 text-label font-bold text-on-navy hover:bg-navy-800'
                      : 'inline-flex min-h-11 items-center rounded-field border-[1.5px] border-border bg-surface px-4 text-label font-bold text-text-strong hover:border-brand-500'
                  }
                >
                  {time}
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** The sticky «احجز الآن» bar with the design's caption; the reason instead while online booking is off. */
async function BookBar({ target, locale }: { target: Target; locale: AppLocale }) {
  const [t, status] = await Promise.all([
    getTranslations({ locale, namespace: 'qrLanding.cta' }),
    getPublicShopStatus(target.shopSlug),
  ]);
  const accepts = status?.acceptsOnlineBookings ?? false;
  return (
    <div className="sticky bottom-0 z-10 border-t border-border bg-surface/95 backdrop-blur-md">
      <div className="mx-auto flex max-w-[720px] flex-col gap-2 px-4 py-3 md:px-6">
        {accepts ? (
          <>
            <ButtonLink
              href={bookHref(target.shopSlug, { pro: target.professionalId ?? undefined })}
              variant="primary"
              size="lg"
              fullWidth
            >
              {t('book')}
            </ButtonLink>
            <p className="text-center text-helper text-text-tertiary">{t('caption')}</p>
          </>
        ) : (
          <p className="text-center text-helper font-bold text-text-secondary">{t('unavailable')}</p>
        )}
      </div>
    </div>
  );
}

/**
 * The QR landing (c-qr 2041–2082, R-CUS-13, DV-A14): "you came in with the shop's code", the shop's identity and live
 * opening, the intro callout, the earliest free times, the services with their prices, and the booking bar. A barber's
 * code opens the same page about that barber (the design describes it without drawing it). The scan is counted from
 * the browser, without tracking; the page is never indexed and points to the shop or barber page as canonical.
 */
export default async function QrLandingPage({ params }: PageProps<'/[locale]/q/[code]'>) {
  const { locale: raw, code } = await params;
  const locale = asLocale(raw);
  const target = await resolveCode(code);
  if (!target) notFound();
  const shop = await getPublicShop(target.shopSlug);
  if (!shop) notFound();
  const professional =
    target.targetType === 'Professional' && target.professionalSlug
      ? await getPublicProfessional(target.shopSlug, target.professionalSlug)
      : null;

  const t = await getTranslations({ locale, namespace: 'qrLanding' });
  const tShop = await getTranslations({ locale, namespace: 'shopPage' });
  const tOpening = await getTranslations({ locale, namespace: 'opening' });
  const api = await getPublicApi();
  const path = { params: { path: { slug: shop.slug } } };
  const [services, packages, status] = professional
    ? [null, null, await getPublicShopStatus(shop.slug)]
    : await Promise.all([
        optional(() => api.GET('/api/v1/public/shops/{slug}/services', path), 'services'),
        optional(() => api.GET('/api/v1/public/shops/{slug}/packages', path), 'packages'),
        getPublicShopStatus(shop.slug),
      ]);

  const kind = professional ? 'Professional' : 'Shop';
  const shopName = localizedName(locale, shop.nameAr, shop.nameEn);
  const title = professional ? localizedName(locale, professional.nameAr, professional.nameEn) : shopName;
  const offers = professional
    ? professional.offers
    : [
        ...(services ?? []).filter((s) => s.onlineBookable).map((s) => ({ ...s, isPackage: false })),
        ...(packages ?? []).map((p) => ({ ...p, isPackage: true })),
      ];
  const opening = status ? openingLabel(tOpening, locale, status, shop.timeZone) : null;
  const where = [shop.location?.district, opening].filter(Boolean).join(' · ');

  return (
    <PublicShell variant="marketing">
      <QrVisitRecorder code={target.code} locale={locale} />
      <article
        className="mx-auto flex w-full max-w-[720px] flex-col"
        data-testid="qr-landing"
        data-target={kind}
      >
        <div className="relative h-[150px] overflow-hidden bg-bg-muted md:mt-4 md:rounded-t-section">
          {shop.coverUrl ? (
            <Image
              src={shop.coverUrl}
              alt=""
              fill
              priority
              sizes="(min-width: 720px) 720px, 100vw"
              className="object-cover"
            />
          ) : (
            <ImagePlaceholder className="h-full w-full" />
          )}
          <span className="absolute start-3.5 top-3.5 inline-flex items-center gap-1.5 rounded-sm bg-navy-900/90 px-3 py-1.5 text-helper font-bold text-on-navy">
            <Icon name="qr" className="size-4" />
            {t(`badge.${kind}`)}
          </span>
        </div>

        <div className="relative -mt-[18px] flex flex-col gap-5 rounded-t-[18px] bg-surface px-4 pt-5 pb-6 md:px-6">
          <header className="flex items-start gap-3">
            {professional ? (
              <div className="-mt-9 shrink-0 rounded-full border-[3px] border-surface">
                <Avatar name={title} src={professional.avatarUrl} size="lg" />
              </div>
            ) : (
              <div className="relative -mt-9 flex size-[58px] shrink-0 items-center justify-center overflow-hidden rounded-[15px] border-[3px] border-surface bg-navy-900 text-[1.375rem] font-extrabold text-on-navy">
                {shop.logoUrl ? (
                  <Image
                    src={shop.logoUrl}
                    alt={tShop('logoAlt', { name: shopName })}
                    fill
                    sizes="58px"
                    className="object-cover"
                  />
                ) : (
                  shopName.charAt(0)
                )}
              </div>
            )}
            <div className="flex min-w-0 flex-1 flex-col gap-1.5">
              <h1 className="flex flex-wrap items-center gap-1.5 text-h2 font-bold text-navy-900">
                {title}
                {!professional && shop.isVerified && (
                  <span title={tShop('verifiedHint')} className="inline-flex text-brand-700">
                    <Icon name="shield" className="size-5" />
                    <span className="sr-only">{tShop('verified')}</span>
                  </span>
                )}
              </h1>
              {professional ? (
                <p className="text-helper text-text-secondary">{t('worksAt', { shop: shopName })}</p>
              ) : null}
              {where && <p className="text-helper text-text-secondary">{where}</p>}
            </div>
            <Link
              href={canonicalPath(target)}
              className="inline-flex min-h-11 shrink-0 items-center text-label font-bold text-text-link hover:underline"
            >
              {t('fullPage')}
            </Link>
          </header>

          <InlineAlert tone="info" title={t(`intro.${kind}`, { name: title })} />

          <Suspense fallback={<Skeleton className="h-20 w-full" />}>
            <EarliestTimes target={target} locale={locale} timeZone={shop.timeZone} />
          </Suspense>

          <section aria-labelledby="qr-services" className="flex flex-col gap-2.5">
            <h2 id="qr-services" className="text-h3 font-bold text-navy-900">
              {t('services.title')}
            </h2>
            {offers.length === 0 ? (
              <EmptyState icon="scissors" title={t('services.empty')} />
            ) : (
              <ul className="flex flex-col gap-2" data-testid="qr-services">
                {offers.map((offer) => (
                  <li
                    key={offer.id}
                    className="flex items-center gap-3 rounded-button border border-border bg-surface px-3.5 py-3"
                  >
                    <div className="flex min-w-0 flex-1 flex-col">
                      <span className="text-label font-bold text-text-primary">
                        {localizedName(locale, offer.nameAr, offer.nameEn)}
                      </span>
                      <span className="text-helper text-text-secondary">
                        {formatDurationMinutes(offer.durationMinutes, locale)}
                      </span>
                    </div>
                    <span className="font-latin text-label font-extrabold text-navy-900">
                      {formatPrice(offer.price, locale, offer.currency)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
        <Suspense fallback={null}>
          <BookBar target={target} locale={locale} />
        </Suspense>
      </article>
    </PublicShell>
  );
}
