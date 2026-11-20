import { useLocale, useTranslations } from 'next-intl';
import { Fragment } from 'react';
import { Badge } from '@/components/ui/Badge';
import { ButtonLink } from '@/components/ui/Button';
import { catalogGroups, type CatalogSection, type CatalogItem, packageSaving } from '@/lib/booking/catalog';
import { bookHref, SHOP_BOOK_FORM_ID } from '@/lib/booking/links';
import { type AppLocale, formatDurationMinutes, formatPrice } from '@/lib/i18n/format';
import { langIfOther, localizedName, localizedText } from '@/lib/i18n/localized';

/**
 * The shop page's services (D-148, «٤-١»): the same section tabs (the shop's own, D-149) as the booking's service step, packages last, each
 * tile with its name, price, length and «احجز» link (or «حضوري فقط»), and a package's saving. The tabs are native radio
 * buttons inside the pills and CSS (`:has(:checked)`) shows the chosen section's tiles: no script on the public page, arrow keys work, and every service
 * stays in the page's HTML for search engines. The radios belong to the «احجز الآن» form (D-157), which sends the tab
 * being shown, so the booking's service step opens on it.
 */
export function ShopServiceCatalog({
  slug,
  items,
  sections,
  bookable,
  onlineIds,
}: {
  slug: string;
  items: CatalogItem[];
  sections: CatalogSection[];
  bookable: boolean;
  /** Services that can be booked online (packages always can, when the shop takes online bookings). */
  onlineIds: string[];
}) {
  const t = useTranslations('shopPage.services');
  const tCatalog = useTranslations('booking.catalog');
  const locale = useLocale() as AppLocale;
  const groups = catalogGroups(items, sections, locale, {
    other: tCatalog('other'),
    packages: tCatalog('packages'),
  });

  const tile = (item: CatalogItem) => {
    const name = localizedName(locale, item.nameAr, item.nameEn);
    const description = localizedText(locale, item.descriptionAr, item.descriptionEn);
    const saving = packageSaving(item, items);
    const online = bookable && (item.kind === 'package' || onlineIds.includes(item.id));
    return (
      <li
        key={`${item.kind}-${item.id}`}
        className="flex min-h-[96px] flex-col justify-between gap-2 rounded-card border border-border bg-surface p-3 shadow-e1"
      >
        <span className="flex min-w-0 flex-col gap-0.5">
          <h3 className="line-clamp-2 text-[0.875rem] leading-snug font-bold text-text-primary">{name}</h3>
          {description && (
            <span lang={langIfOther(description, locale)} className="truncate text-badge text-text-secondary">
              {description.text}
            </span>
          )}
          {saving > 0 && (
            <Badge tone="success" size="sm" className="self-start">
              {tCatalog('saves', { amount: formatPrice(saving, locale, item.currency) })}
            </Badge>
          )}
        </span>
        <span className="flex flex-wrap items-center justify-between gap-2">
          <span className="flex flex-col">
            <span className="font-latin text-[0.9375rem] font-extrabold whitespace-nowrap text-navy-900">
              {formatPrice(item.price, locale, item.currency)}
            </span>
            <span className="text-badge whitespace-nowrap text-text-secondary">
              {formatDurationMinutes(item.durationMinutes, locale)}
            </span>
          </span>
          {online ? (
            <ButtonLink
              href={
                item.kind === 'package'
                  ? bookHref(slug, { package: item.id, step: 'service' })
                  : bookHref(slug, { service: item.id, step: 'service' })
              }
              variant="secondary"
              size="xs"
              aria-label={t('bookLabel', { name })}
            >
              {t('book')}
            </ButtonLink>
          ) : (
            <Badge tone="neutral" size="sm">
              {t('walkInOnly')}
            </Badge>
          )}
        </span>
      </li>
    );
  };

  const list = (g: (typeof groups)[number]) => (
    <ul className="grid grid-cols-2 gap-2.5 md:grid-cols-3">{g.items.map(tile)}</ul>
  );
  if (groups.length === 1) {
    return <div data-testid="service-catalog">{list(groups[0]!)}</div>;
  }

  return (
    <fieldset className="flex min-w-0 flex-wrap gap-2" data-testid="service-catalog">
      <legend className="sr-only">{t('title')}</legend>
      {groups.map((g, index) => (
        // Each tab: a pill holding its radio, then its tiles, shown only while that pill's radio is checked.
        <Fragment key={g.key}>
          <label className="relative inline-flex min-h-11 shrink-0 cursor-pointer items-center rounded-full border border-border-input bg-surface px-4 text-label font-bold whitespace-nowrap text-text-strong transition-colors hover:border-brand-500 has-checked:border-navy-900 has-checked:bg-navy-900 has-checked:text-on-navy has-focus-visible:shadow-[var(--focus-ring)]">
            <input
              type="radio"
              name="section"
              value={g.key}
              form={SHOP_BOOK_FORM_ID}
              defaultChecked={index === 0}
              className="sr-only"
            />
            {g.label}
          </label>
          <div className="order-1 hidden basis-full pt-1 [label:has(:checked)+&]:block">{list(g)}</div>
        </Fragment>
      ))}
    </fieldset>
  );
}
