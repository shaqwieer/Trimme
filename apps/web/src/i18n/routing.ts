import { defineRouting } from 'next-intl/routing';

/** Arabic (RTL) is the primary locale; English (LTR) is secondary (spec §6, D-029). */
export const routing = defineRouting({
  locales: ['ar', 'en'],
  defaultLocale: 'ar',
  localePrefix: 'always',
});

export type Locale = (typeof routing.locales)[number];

export const localeDirection: Record<Locale, 'rtl' | 'ltr'> = {
  ar: 'rtl',
  en: 'ltr',
};

/** Narrows a route parameter to a supported locale (unknown values fall back to Arabic). */
export function asLocale(value: string): Locale {
  return value === 'en' ? 'en' : 'ar';
}
