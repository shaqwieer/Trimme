import type { AppLocale } from './format';

/**
 * The name to show for a record whose English name is optional (D-070): English when present in the English UI,
 * otherwise Arabic. Arabic is always required, so there is always something to show.
 */
export function localizedName(locale: AppLocale | string, nameAr: string, nameEn?: string | null): string {
  return locale === 'en' && nameEn ? nameEn : nameAr;
}
