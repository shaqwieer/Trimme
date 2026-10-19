import { defineRouting } from 'next-intl/routing';

/** Arabic (RTL) is the primary locale; English and Turkish (LTR) are secondary (spec §6, D-029). */
export const routing = defineRouting({
  locales: ['ar', 'en', 'tr'],
  defaultLocale: 'ar',
  localePrefix: 'always',
});

export type Locale = (typeof routing.locales)[number];

export const localeDirection: Record<Locale, 'rtl' | 'ltr'> = {
  ar: 'rtl',
  en: 'ltr',
  tr: 'ltr',
};

/**
 * The message that names each locale in its own language in the language pickers (`account.profile`,
 * `auth.completeProfile`).
 */
export const LANGUAGE_NAME_KEY = {
  ar: 'arabic',
  en: 'english',
  tr: 'turkish',
} as const satisfies Record<Locale, string>;

const isLocale = (value: string): value is Locale => (routing.locales as readonly string[]).includes(value);

/** Narrows a route parameter to a supported locale (unknown values fall back to Arabic). */
export function asLocale(value: string): Locale {
  return isLocale(value) ? value : routing.defaultLocale;
}
