import type { AppLocale } from '@/lib/i18n/format';
import { localizedName } from '@/lib/i18n/localized';

/** A shop's catalog grouped the way the app shows it (D-148, «٤-١»); shared by the shop page and the booking wizard. */
export type CatalogSection = { id: string; nameAr: string; nameEn: string | null };

export type CatalogItem = {
  kind: 'service' | 'package';
  id: string;
  nameAr: string;
  nameEn: string | null;
  descriptionAr: string | null;
  descriptionEn: string | null;
  price: number;
  currency: string;
  durationMinutes: number;
  /** A service's section in its shop (the tab it sits in, D-149); null for «أخرى». */
  sectionId?: string | null;
  /** A package's services (to work out what it saves). */
  itemIds?: string[];
};

export type CatalogGroup<T extends CatalogItem = CatalogItem> = { key: string; label: string; items: T[] };

/** What a package saves against its services' own prices (D-148: «توفّر ١٥»); 0 when it saves nothing. */
export function packageSaving(pkg: CatalogItem, items: CatalogItem[]): number {
  if (pkg.kind !== 'package' || !pkg.itemIds?.length) return 0;
  const prices = pkg.itemIds.map((id) => items.find((i) => i.kind === 'service' && i.id === id)?.price);
  if (prices.some((p) => p === undefined)) return 0;
  const total = prices.reduce<number>((sum, p) => sum + (p ?? 0), 0);
  return Math.max(0, Math.round((total - pkg.price) * 100) / 100);
}

/**
 * The tabs and what sits under each: the shop's own sections in the owner's order (only those with services, D-149),
 * then «أخرى» for services in no section, then the packages last.
 */
export function catalogGroups<T extends CatalogItem>(
  items: T[],
  sections: CatalogSection[],
  locale: AppLocale,
  labels: { other: string; packages: string },
): CatalogGroup<T>[] {
  const services = items.filter((i) => i.kind === 'service');
  const packages = items.filter((i) => i.kind === 'package');
  const known = new Set(sections.map((c) => c.id));
  const other = services.filter((s) => !s.sectionId || !known.has(s.sectionId));
  return [
    ...sections
      .map((c) => ({
        key: c.id,
        label: localizedName(locale, c.nameAr, c.nameEn),
        items: services.filter((s) => s.sectionId === c.id),
      }))
      .filter((g) => g.items.length > 0),
    ...(other.length > 0 ? [{ key: 'other', label: labels.other, items: other }] : []),
    ...(packages.length > 0 ? [{ key: 'packages', label: labels.packages, items: packages }] : []),
  ];
}
