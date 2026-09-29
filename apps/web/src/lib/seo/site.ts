import type { Metadata } from 'next';

/**
 * The public site's absolute origin (canonical URLs, hreflang, Open Graph, sitemap, R-WEB-10). Set
 * `TRIMME_SITE_URL` in every deployed environment; the default is for local development only.
 */
export function siteUrl(): URL {
  const configured = process.env.TRIMME_SITE_URL;
  try {
    return new URL(configured && configured.length > 0 ? configured : 'http://localhost:3000');
  } catch {
    return new URL('http://localhost:3000');
  }
}

export function absoluteUrl(path: string): string {
  return new URL(path, siteUrl()).toString();
}

/**
 * Canonical and `hreflang` alternates for a public page that exists in both locales. `path` is the locale-free path
 * with its query, for example `/shops/al-asala` or `/shops?city=…`; Arabic is the `x-default` (spec §6, D-029).
 */
export function localizedAlternates(path: string, locale: string): NonNullable<Metadata['alternates']> {
  const suffix = path === '/' ? '' : path;
  return {
    canonical: `/${locale}${suffix}`,
    languages: { ar: `/ar${suffix}`, en: `/en${suffix}`, 'x-default': `/ar${suffix}` },
  };
}

/** Open Graph locale names. */
export const OG_LOCALE: Record<string, string> = { ar: 'ar_SA', en: 'en_US' };

/** Private or personalised pages: never indexed (R-NEG-10). */
export const NO_INDEX: Metadata['robots'] = { index: false, follow: false };
