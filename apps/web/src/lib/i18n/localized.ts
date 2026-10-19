import { asLocale } from '@/i18n/routing';
import type { AppLocale } from './format';

/** The languages record data is kept in: shops, services and professionals have Arabic and English fields only. */
export type ContentLanguage = 'ar' | 'en';

/**
 * Which version of a record's data a page in `locale` shows: Arabic on Arabic pages, English everywhere else (Turkish
 * pages show the English fields). Unknown values count as Arabic, like {@link asLocale}.
 */
export function contentLanguage(locale: AppLocale | string): ContentLanguage {
  return asLocale(locale) === 'ar' ? 'ar' : 'en';
}

/**
 * The name to show for a record whose English name is optional (D-070): English when present in the English and
 * Turkish UI, otherwise Arabic. Arabic is always required, so there is always something to show.
 */
export function localizedName(locale: AppLocale | string, nameAr: string, nameEn?: string | null): string {
  return contentLanguage(locale) === 'en' && nameEn ? nameEn : nameAr;
}

/** Text from a record with an Arabic and an optional English version, with the language it is actually in. */
export type LocalizedText = { text: string; lang: AppLocale };

/**
 * Longer optional text (descriptions, D-070): the page's content language when present, otherwise the other one,
 * which the caller marks with `lang` (WCAG 3.1.2) through {@link langIfOther}. Null when neither version exists.
 */
export function localizedText(
  locale: AppLocale | string,
  ar: string | null | undefined,
  en: string | null | undefined,
): LocalizedText | null {
  const preferred = contentLanguage(locale);
  const other: ContentLanguage = preferred === 'en' ? 'ar' : 'en';
  const versions = { ar, en };
  if (versions[preferred]) return { text: versions[preferred], lang: preferred };
  if (versions[other]) return { text: versions[other], lang: other };
  return null;
}

/** The `lang` attribute for text shown on a page in `locale`: set only when the text is in another language. */
export function langIfOther(text: LocalizedText, locale: AppLocale | string): string | undefined {
  return text.lang === locale ? undefined : text.lang;
}
