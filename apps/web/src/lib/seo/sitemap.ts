import { type Locale, routing } from '@/i18n/routing';
import { absoluteUrl } from './site';

/** A public page that exists in every locale, as a locale-free path (`/`, `/shops/al-asala`). */
export type SitemapPage = { path: string; lastModified?: string };

const XML_ESCAPES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&apos;',
};
const xml = (value: string) => value.replace(/[&<>"']/g, (c) => XML_ESCAPES[c]!);

const localized = (locale: string, path: string) => absoluteUrl(`/${locale}${path === '/' ? '' : path}`);

/** The per-locale sitemap files listed by the index (R-WEB-10, Phase 17). */
export function sitemapFiles(): string[] {
  return routing.locales.map((locale) => `/sitemaps/${locale}.xml`);
}

export function sitemapIndexXml(files: string[]): string {
  const entries = files.map((file) => `<sitemap><loc>${xml(absoluteUrl(file))}</loc></sitemap>`).join('');
  return `<?xml version="1.0" encoding="UTF-8"?><sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${entries}</sitemapindex>`;
}

/** One locale's pages, each with its alternates in every locale and the Arabic `x-default` (D-029). */
export function localeSitemapXml(locale: Locale, pages: SitemapPage[]): string {
  const entries = pages
    .map((page) => {
      const alternates = [
        ...routing.locales.map((other) => [other, localized(other, page.path)] as const),
        ['x-default', localized(routing.defaultLocale, page.path)] as const,
      ]
        .map(([lang, href]) => `<xhtml:link rel="alternate" hreflang="${lang}" href="${xml(href)}"/>`)
        .join('');
      const lastModified = page.lastModified ? `<lastmod>${xml(page.lastModified)}</lastmod>` : '';
      return `<url><loc>${xml(localized(locale, page.path))}</loc>${lastModified}${alternates}</url>`;
    })
    .join('');
  return `<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">${entries}</urlset>`;
}

export function xmlResponse(body: string): Response {
  return new Response(body, {
    headers: { 'Content-Type': 'application/xml; charset=utf-8', 'Cache-Control': 'public, max-age=300' },
  });
}
