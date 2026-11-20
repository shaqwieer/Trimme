'use client';

import { useLocale, useTranslations } from 'next-intl';
import type { ReactNode } from 'react';
import { CatalogTabs } from '@/components/booking/CatalogTabs';
import { catalogGroups, type CatalogSection, type CatalogItem, packageSaving } from '@/lib/booking/catalog';
import { type AppLocale, formatNumber, formatPrice } from '@/lib/i18n/format';
import { langIfOther, localizedName, localizedText } from '@/lib/i18n/localized';

export type { CatalogSection, CatalogItem };

/** What a choosable tile needs (the booking's service step draws it). */
export type CatalogChoice = {
  item: CatalogItem;
  name: string;
  description?: { text: string; lang?: string };
  /** «توفّر ١٥» for a package that saves something. */
  badge?: string;
  checked: boolean;
};

/**
 * The booking's service step (D-148, «٤-١»/«٤-٢»): the shop's services in its own section tabs (D-149), packages last, as choosable
 * tiles two to a row on a phone. A tab counts the items chosen from it in a small circle, never «٠»; a package says
 * what it saves. The shop page shows the same tabs from the server (`ShopServiceCatalog`).
 */
export function ServiceCatalog({
  items,
  sections,
  selected,
  choice,
  label,
  opened,
}: {
  items: CatalogItem[];
  sections: CatalogSection[];
  /** The chosen items. */
  selected: CatalogItem[];
  /**
   * The tab to open when nothing is chosen yet: the one the shop page was showing (D-157). A tab the booking does not
   * have (only walk-in services) opens the first.
   */
  opened?: string;
  /** Draws a choosable tile. */
  choice: (tile: CatalogChoice) => ReactNode;
  /** The tab list's accessible name. */
  label: string;
}) {
  const t = useTranslations('booking.catalog');
  const locale = useLocale() as AppLocale;
  const groups = catalogGroups(items, sections, locale, { other: t('other'), packages: t('packages') });
  const isChosen = (item: CatalogItem) => selected.some((s) => s.kind === item.kind && s.id === item.id);

  return (
    <CatalogTabs
      label={label}
      initial={groups.find((g) => g.items.some(isChosen))?.key ?? opened}
      tabs={groups.map((g) => {
        const count = g.items.filter(isChosen).length;
        return {
          key: g.key,
          label: g.label,
          badge:
            count > 0 ? { text: formatNumber(count, locale), label: t('chosenCount', { count }) } : undefined,
        };
      })}
      panels={Object.fromEntries(
        groups.map((g) => [
          g.key,
          <div key={g.key} className="grid grid-cols-2 gap-2.5 md:grid-cols-3">
            {g.items.map((item) => {
              const description = localizedText(locale, item.descriptionAr, item.descriptionEn);
              const saving = packageSaving(item, items);
              return (
                <div key={`${item.kind}-${item.id}`} className="contents">
                  {choice({
                    item,
                    name: localizedName(locale, item.nameAr, item.nameEn),
                    description: description
                      ? { text: description.text, lang: langIfOther(description, locale) }
                      : undefined,
                    badge:
                      saving > 0
                        ? t('saves', { amount: formatPrice(saving, locale, item.currency) })
                        : undefined,
                    checked: isChosen(item),
                  })}
                </div>
              );
            })}
          </div>,
        ]),
      )}
    />
  );
}
