import Image from 'next/image';
import { useLocale, useTranslations } from 'next-intl';
import { Badge } from '@/components/ui/Badge';
import { ShopCard } from '@/components/ui/cards';
import { Icon } from '@/components/ui/icons';
import { ImagePlaceholder } from '@/components/ui/Avatar';
import { RatingStars } from '@/components/ui/Rating';
import { Link } from '@/i18n/navigation';
import type { ShopSearchItem } from '@/lib/api/public-types';
import { isTomorrow, openingLabel } from '@/lib/discovery/opening';
import {
  type AppLocale,
  formatDistanceKm,
  formatDurationMinutes,
  formatPrice,
  formatTime,
} from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

export const shopHref = (slug: string) => `/shops/${slug}`;

/** The shop's name in the page language (English is always set for shops). */
function shopName(shop: ShopSearchItem, locale: AppLocale): string {
  return locale === 'en' ? shop.nameEn || shop.nameAr : shop.nameAr;
}

/** Grid card (landing "top rated", `/shops`): the design-system ShopCard fed with discovery data. */
export function DiscoveryShopCard({
  shop,
  headingLevel = 3,
}: {
  shop: ShopSearchItem;
  headingLevel?: 2 | 3;
}) {
  const locale = useLocale() as AppLocale;
  const t = useTranslations('opening');

  return (
    <ShopCard
      headingLevel={headingLevel}
      shop={{
        href: shopHref(shop.slug),
        name: shopName(shop, locale),
        coverUrl: shop.coverUrl,
        verified: shop.isVerified,
        rating: shop.rating,
        reviewCount: shop.reviewCount,
        district: shop.district ?? shop.city ?? '',
        distanceKm: shop.distanceKm,
        openingLabel: openingLabel(t, locale, shop, shop.timeZone),
        isOpen: shop.isOpenNow,
        fromPrice: shop.minPrice,
      }}
    />
  );
}

function RatingLine({ shop, locale }: { shop: ShopSearchItem; locale: AppLocale }) {
  return (
    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-helper text-text-secondary">
      {shop.reviewCount > 0 && <RatingStars value={shop.rating} count={shop.reviewCount} />}
      {shop.reviewCount > 0 && shop.district && (
        <span aria-hidden="true" className="text-border-strong">
          ·
        </span>
      )}
      {shop.district && <span>{shop.district}</span>}
      {shop.distanceKm != null && (
        <>
          <span aria-hidden="true" className="text-border-strong">
            ·
          </span>
          <span className="font-latin font-semibold text-text-strong">
            {formatDistanceKm(shop.distanceKm, locale)}
          </span>
        </>
      )}
    </p>
  );
}

/** Home "near you" card (c-home 1022–1040): thumbnail, name, rating, open badge, distance and the lowest price. */
export function ShopListCard({ shop }: { shop: ShopSearchItem }) {
  const locale = useLocale() as AppLocale;
  const t = useTranslations('opening');
  const tCard = useTranslations('ui.shopCard');

  return (
    <article className="relative flex gap-3 rounded-card border border-border bg-surface p-3 shadow-e1 transition-shadow hover:shadow-e2">
      <div className="relative size-[84px] shrink-0 overflow-hidden rounded-field">
        {shop.coverUrl || shop.logoUrl ? (
          <Image src={(shop.coverUrl ?? shop.logoUrl)!} alt="" fill sizes="84px" className="object-cover" />
        ) : (
          <ImagePlaceholder className="h-full w-full" />
        )}
      </div>
      <div className="flex min-w-0 flex-1 flex-col justify-between gap-1.5">
        <h3 className="flex items-center gap-1.5 text-[0.96875rem] font-bold text-text-primary">
          <Link
            href={shopHref(shop.slug)}
            className="truncate after:absolute after:inset-0 after:content-['']"
          >
            {shopName(shop, locale)}
          </Link>
          {shop.isVerified && (
            <Icon name="shield" label={tCard('verified')} className="size-4 shrink-0 text-brand-700" />
          )}
        </h3>
        <RatingLine shop={shop} locale={locale} />
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Badge tone={shop.isOpenNow ? 'success' : 'neutral'} size="sm">
            {shop.isOpenNow ? t('open') : openingLabel(t, locale, shop, shop.timeZone)}
          </Badge>
          <span className="text-helper text-text-secondary">
            {tCard('from', { price: formatPrice(shop.minPrice, locale, shop.currency) })}
          </span>
        </div>
      </div>
    </article>
  );
}

/**
 * Search result card (c-home 1092–1110): name, rating · district · distance, open badge; then the matched service with
 * its price and duration, and the earliest bookable time (computed live by the API, mapRules #2).
 */
export function ShopResultCard({ shop }: { shop: ShopSearchItem }) {
  const locale = useLocale() as AppLocale;
  const t = useTranslations('search');
  const tOpen = useTranslations('opening');
  const tCard = useTranslations('ui.shopCard');
  const offer = shop.matchedOffer;

  return (
    <article className="relative flex flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-e1 transition-shadow hover:shadow-e2">
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h3 className="flex items-center gap-1.5 text-[0.96875rem] font-bold text-text-primary">
            <Link href={shopHref(shop.slug)} className="after:absolute after:inset-0 after:content-['']">
              {shopName(shop, locale)}
            </Link>
            {shop.isVerified && (
              <Icon name="shield" label={tCard('verified')} className="size-4 shrink-0 text-brand-700" />
            )}
          </h3>
          <RatingLine shop={shop} locale={locale} />
        </div>
        <Badge tone={shop.isOpenNow ? 'success' : 'neutral'} size="sm" className="shrink-0">
          {shop.isOpenNow ? tOpen('open') : tOpen('closed')}
        </Badge>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2 border-t border-dashed border-border-dashed pt-3">
        <span className="text-helper text-text-secondary">
          {offer
            ? t('matched', {
                service: localizedName(locale, offer.nameAr, offer.nameEn),
                price: formatPrice(offer.price, locale, offer.currency),
                duration: formatDurationMinutes(offer.durationMinutes, locale),
              })
            : tCard('from', { price: formatPrice(shop.minPrice, locale, shop.currency) })}
        </span>
        {!shop.acceptsOnlineBookings ? (
          <Badge tone="warning" size="sm">
            {t('notBookable')}
          </Badge>
        ) : shop.earliestSlotAt ? (
          <Badge tone="success" size="sm">
            {isTomorrow(shop.earliestSlotAt, shop.timeZone)
              ? t('tomorrow', { time: formatTime(shop.earliestSlotAt, locale, shop.timeZone) })
              : t('earliest', { time: formatTime(shop.earliestSlotAt, locale, shop.timeZone) })}
          </Badge>
        ) : null}
      </div>
    </article>
  );
}
