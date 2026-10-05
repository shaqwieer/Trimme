import type { ReactNode } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { getTranslations } from 'next-intl/server';
import { Avatar } from '@/components/ui/Avatar';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { RatingDistribution } from '@/components/ui/charts';
import { Pagination } from '@/components/ui/data';
import { Icon } from '@/components/ui/icons';
import { RatingStars } from '@/components/ui/Rating';
import { TagChip } from '@/components/ui/selection';
import { EmptyState } from '@/components/ui/states';
import { Link } from '@/i18n/navigation';
import type {
  OpeningInterval,
  PublicPackage,
  PublicProfessional,
  PublicReview,
  PublicReviews,
  PublicService,
  PublicShop,
} from '@/lib/api/public-types';
import { durationParts, minuteOfDay, relativeTime } from '@/lib/discovery/format';
import { isTomorrow } from '@/lib/discovery/opening';
import { bookHref, getPublicShop, getPublicShopStatus } from '@/lib/discovery/shop-data';
import { type AppLocale, formatDurationMinutes, formatPrice, formatRating, formatTime } from '@/lib/i18n/format';
import { langIfOther, type LocalizedText, localizedName, localizedText } from '@/lib/i18n/localized';
import { ShopMiniMap } from './ShopMiniMap';

const WEEK: OpeningInterval['day'][] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** Services then packages, each with price, duration and a booking link (DV-S12: packages get their own section). */
export function ServicesPanel({
  slug,
  services,
  packages,
  bookable,
}: {
  slug: string;
  services: PublicService[];
  packages: PublicPackage[];
  bookable: boolean;
}) {
  const t = useTranslations('shopPage.services');
  const locale = useLocale() as AppLocale;
  if (services.length === 0 && packages.length === 0) return <EmptyState icon="scissors" title={t('empty')} />;

  const row = (key: string, name: string, detail: LocalizedText | null, price: number, currency: string, minutes: number, href: string | null) => (
    <li key={key} className="flex items-center justify-between gap-3 rounded-card border border-border bg-surface p-4 shadow-e1">
      <div className="flex min-w-0 flex-col gap-1">
        <h3 className="text-label font-bold text-text-primary">{name}</h3>
        {detail && (
          <p lang={langIfOther(detail, locale)} className="text-helper text-text-secondary">
            {detail.text}
          </p>
        )}
        <p className="text-helper text-text-tertiary">{formatDurationMinutes(minutes, locale)}</p>
      </div>
      <div className="flex shrink-0 items-center gap-3">
        <span className="font-latin text-[1.0625rem] font-extrabold text-navy-900">{formatPrice(price, locale, currency)}</span>
        {href ? (
          <ButtonLink href={href} variant="secondary" size="xs" aria-label={t('bookLabel', { name })}>
            {t('book')}
          </ButtonLink>
        ) : (
          <Badge tone="neutral" size="sm">
            {t('walkInOnly')}
          </Badge>
        )}
      </div>
    </li>
  );

  return (
    <div className="flex flex-col gap-6">
      {services.length > 0 && (
        <section aria-labelledby="services-heading" className="flex flex-col gap-3">
          <h2 id="services-heading" className="sr-only">
            {t('title')}
          </h2>
          <ul className="flex flex-col gap-3">
            {services.map((s) =>
              row(
                s.id,
                localizedName(locale, s.nameAr, s.nameEn),
                localizedText(locale, s.descriptionAr, s.descriptionEn),
                s.price,
                s.currency,
                s.durationMinutes,
                bookable && s.onlineBookable ? bookHref(slug, { service: s.id }) : null,
              ),
            )}
          </ul>
        </section>
      )}
      {packages.length > 0 && (
        <section aria-labelledby="packages-heading" className="flex flex-col gap-3">
          <h2 id="packages-heading" className="text-h3 font-bold text-navy-900">
            {t('packages')}
          </h2>
          <ul className="flex flex-col gap-3">
            {packages.map((p) =>
              row(
                p.id,
                localizedName(locale, p.nameAr, p.nameEn),
                {
                  text: t('includes', { items: p.items.map((i) => localizedName(locale, i.nameAr, i.nameEn)).join(locale === 'ar' ? '، ' : ', ') }),
                  lang: locale,
                },
                p.price,
                p.currency,
                p.durationMinutes,
                bookable ? bookHref(slug, { package: p.id }) : null,
              ),
            )}
          </ul>
        </section>
      )}
    </div>
  );
}

/** Each professional's next free time today or tomorrow (live, D-091); fetched with the shop status. */
async function NextTime({ slug, professionalId, locale }: { slug: string; professionalId: string; locale: AppLocale }) {
  const t = await getTranslations({ locale, namespace: 'shopPage.pros' });
  const status = await getPublicShopStatus(slug);
  const next = status?.professionals.find((p) => p.professionalId === professionalId)?.nextAvailableAt;
  if (!status || !status.acceptsOnlineBookings) return null;
  if (!next) {
    return (
      <Badge tone="neutral" size="sm">
        {t('none')}
      </Badge>
    );
  }
  const timeZone = (await getPublicShop(slug))?.timeZone ?? 'Asia/Riyadh';
  const time = formatTime(next, locale, timeZone);
  const tomorrow = isTomorrow(next, timeZone);
  return (
    <Badge tone={tomorrow ? 'warning' : 'success'} size="sm">
      {tomorrow ? t('tomorrow', { time }) : t('today', { time })}
    </Badge>
  );
}

/** The shop's professionals with their next free time and a link to each profile (c-shop 1298–1312). */
export function ProfessionalsPanel({
  slug,
  professionals,
  nextTimes,
}: {
  slug: string;
  professionals: PublicProfessional[];
  /** The live next-time chip per professional (a Suspense island rendered by the page). */
  nextTimes: Record<string, ReactNode>;
}) {
  const t = useTranslations('shopPage.pros');
  const locale = useLocale() as AppLocale;
  if (professionals.length === 0) return <EmptyState icon="users" title={t('empty')} />;

  return (
    <ul className="flex flex-col gap-3">
      {professionals.map((p) => {
        const name = localizedName(locale, p.nameAr, p.nameEn);
        const specialty = p.specialtyAr || p.specialtyEn ? localizedName(locale, p.specialtyAr ?? '', p.specialtyEn) : null;
        return (
          <li key={p.id} className="relative flex items-center gap-3 rounded-card border border-border bg-surface p-4 shadow-e1 hover:shadow-e2">
            <Avatar name={name} src={p.avatarUrl} size="lg" />
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <h3 className="text-label font-bold text-text-primary">
                <Link href={`/shops/${slug}/professionals/${p.slug}`} className="after:absolute after:inset-0 after:content-['']">
                  {name}
                </Link>
              </h3>
              {specialty && <p className="text-helper text-text-secondary">{specialty}</p>}
              {p.reviewCount > 0 && <RatingStars value={p.rating} count={p.reviewCount} />}
            </div>
            <div className="shrink-0">{nextTimes[p.id]}</div>
          </li>
        );
      })}
    </ul>
  );
}

export { NextTime };

function ReviewItem({ review }: { review: PublicReview }) {
  const locale = useLocale() as AppLocale;
  return (
    <li className="flex flex-col gap-2 rounded-card border border-border bg-surface p-4 shadow-e1">
      <div className="flex items-center gap-3">
        <Avatar name={review.authorName} size="md" />
        <div className="flex min-w-0 flex-1 flex-col">
          {/* Reviews keep the language they were written in, so their direction follows the text (bidi-safe). */}
          <span dir="auto" className="text-label font-bold text-text-primary">
            {review.authorName}
          </span>
          <span className="text-helper text-text-tertiary">
            {relativeTime(review.createdAt, locale)}
            {' · '}
            {localizedName(locale, review.itemNameAr, review.itemNameEn)}
          </span>
        </div>
        <RatingStars value={review.rating} showValue={false} />
      </div>
      {review.comment && (
        <p dir="auto" className="text-body text-text-strong">
          {review.comment}
        </p>
      )}
    </li>
  );
}

/** Rating summary (average, stars, distribution) and the published reviews, paged in the URL (c-shop 1313–1339). */
export function ReviewsPanel({
  reviews,
  hrefForPage,
  showNote = true,
}: {
  reviews: PublicReviews | null;
  hrefForPage: (page: number) => string;
  showNote?: boolean;
}) {
  const t = useTranslations('reviewsList');
  const locale = useLocale() as AppLocale;
  if (!reviews || reviews.summary.count === 0) {
    return <EmptyState icon="star" title={t('empty.title')} body={t('empty.body')} />;
  }
  const [one = 0, two = 0, three = 0, four = 0, five = 0] = reviews.summary.histogram;

  return (
    <div className="flex flex-col gap-5">
      <div className="grid items-center gap-5 rounded-card border border-border bg-surface p-5 shadow-e1 sm:grid-cols-[auto_1fr]">
        <div className="flex flex-col items-center gap-1 sm:px-4">
          <span className="font-latin text-[2.25rem] leading-none font-extrabold text-navy-900">{formatRating(reviews.summary.average, locale)}</span>
          <RatingStars value={reviews.summary.average} showValue={false} size="md" />
          <span className="text-helper text-text-secondary">{t('basedOn', { count: reviews.summary.count })}</span>
        </div>
        <RatingDistribution counts={{ 1: one, 2: two, 3: three, 4: four, 5: five }} />
      </div>
      {showNote && <p className="text-helper text-text-tertiary">{t('note')}</p>}
      <ul className="flex flex-col gap-3">
        {reviews.reviews.items.map((review) => (
          <ReviewItem key={review.id} review={review} />
        ))}
      </ul>
      {reviews.reviews.total > reviews.reviews.pageSize && (
        <Pagination page={reviews.reviews.page} pageSize={reviews.reviews.pageSize} total={reviews.reviews.total} hrefForPage={hrefForPage} />
      )}
    </div>
  );
}

/** Description, weekly hours (closed days kept at full contrast, DV-T11), policies from settings, amenities, map. No phone (D-129). */
export function AboutPanel({ shop, name }: { shop: PublicShop; name: string }) {
  const t = useTranslations('shopPage.about');
  const tPage = useTranslations('shopPage');
  const tDays = useTranslations('shopSchedule.days');
  const tAmenity = useTranslations('amenity');
  const tCategory = useTranslations('shopCategory');
  const locale = useLocale() as AppLocale;
  const description = localizedText(locale, shop.descriptionAr, shop.descriptionEn);
  const cancel = durationParts(shop.cancellationCutoffMinutes);
  const cancelText = tPage(`duration.${cancel.unit}`, { count: cancel.count });

  return (
    <div className="flex flex-col gap-6">
      {description && (
        <section aria-labelledby="about-description" className="flex flex-col gap-2">
          <h2 id="about-description" className="text-h3 font-bold text-navy-900">
            {t('description')}
          </h2>
          <p lang={langIfOther(description, locale)} className="text-body whitespace-pre-line text-text-strong">
            {description.text}
          </p>
        </section>
      )}

      <section aria-labelledby="about-hours" className="flex flex-col gap-2">
        <h2 id="about-hours" className="text-h3 font-bold text-navy-900">
          {t('hours')}
        </h2>
        {shop.openingHours.length === 0 ? (
          <p className="text-body text-text-secondary">{t('noHours')}</p>
        ) : (
          <table className="w-full text-body">
            <tbody>
              {WEEK.map((day) => {
                const windows = shop.openingHours.filter((i) => i.day === day).sort((a, b) => a.startMinute - b.startMinute);
                return (
                  <tr key={day} className="border-b border-border-row last:border-0">
                    <th scope="row" className="py-2.5 text-start font-bold text-text-primary">
                      {tDays(day)}
                    </th>
                    <td className="py-2.5 text-end text-text-strong">
                      {windows.length === 0
                        ? t('closed')
                        : windows.map((w) => `${minuteOfDay(w.startMinute, locale)} — ${minuteOfDay(w.endMinute, locale)}`).join(locale === 'ar' ? '، ' : ', ')}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </section>

      <section aria-labelledby="about-policies" className="flex flex-col gap-2">
        <h2 id="about-policies" className="text-h3 font-bold text-navy-900">
          {t('policies')}
        </h2>
        <ul className="flex flex-col gap-2 text-body text-text-strong">
          <li className="flex items-center gap-2">
            <Icon name="clock" className="size-5 text-brand-600" />
            {t('cancel', { duration: cancelText })}
          </li>
          <li className="flex items-center gap-2">
            <Icon name="store" className="size-5 text-brand-600" />
            {t('payAtShop')}
          </li>
        </ul>
      </section>

      {shop.amenities.length > 0 && (
        <section aria-labelledby="about-amenities" className="flex flex-col gap-2">
          <h2 id="about-amenities" className="text-h3 font-bold text-navy-900">
            {t('amenities')}
          </h2>
          <ul className="flex flex-wrap gap-2">
            {shop.amenities.map((amenity) => (
              <li key={amenity}>
                <TagChip>{tAmenity(amenity)}</TagChip>
              </li>
            ))}
          </ul>
        </section>
      )}

      <dl className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-0.5">
          <dt className="text-helper text-text-secondary">{t('category')}</dt>
          <dd className="text-body font-bold text-text-primary">{tCategory(shop.category)}</dd>
        </div>
      </dl>

      {shop.location && <ShopMiniMap latitude={shop.location.latitude} longitude={shop.location.longitude} label={tPage('mapLabel', { name })} />}
    </div>
  );
}
