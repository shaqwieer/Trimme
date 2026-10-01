import { describe, expect, it } from 'vitest';
import { aggregateRatingLd, breadcrumbLd, openingHoursLd, serializeJsonLd } from './jsonld';
import { localizedAlternates } from './site';

describe('structured data (R-WEB-11)', () => {
  it('jsonld_aggregateRating_absent_without_reviews', () => {
    expect(aggregateRatingLd(0, 0)).toBeUndefined();
    expect(aggregateRatingLd(4.5, 2)).toEqual({
      '@type': 'AggregateRating',
      ratingValue: 4.5,
      reviewCount: 2,
      bestRating: 5,
      worstRating: 1,
    });
  });

  it('escapes shop text so it cannot close the script element', () => {
    const json = serializeJsonLd({ name: '</script><script>alert(1)</script> & co' });
    expect(json).not.toContain('<');
    expect(json).not.toContain('>');
    expect(json).not.toContain('&');
    expect(JSON.parse(json)).toEqual({ name: '</script><script>alert(1)</script> & co' });
  });

  it('splits an opening window that passes midnight into two days', () => {
    expect(openingHoursLd([{ day: 'Saturday', startMinute: 21 * 60, endMinute: 26 * 60 }])).toEqual([
      {
        '@type': 'OpeningHoursSpecification',
        dayOfWeek: 'https://schema.org/Saturday',
        opens: '21:00',
        closes: '23:59',
      },
      {
        '@type': 'OpeningHoursSpecification',
        dayOfWeek: 'https://schema.org/Sunday',
        opens: '00:00',
        closes: '02:00',
      },
    ]);
  });

  it('numbers breadcrumb positions', () => {
    const list = breadcrumbLd([
      { name: 'Home', url: 'https://trimme.test/ar' },
      { name: 'Shops', url: 'https://trimme.test/ar/shops' },
    ]);
    expect((list.itemListElement as Array<{ position: number }>).map((item) => item.position)).toEqual([
      1, 2,
    ]);
  });
});

describe('public_pages_have_hreflang_and_canonical (R-WEB-10)', () => {
  it('points the canonical at the page locale and lists both locales with Arabic as x-default', () => {
    expect(localizedAlternates('/shops/al-asala', 'en')).toEqual({
      canonical: '/en/shops/al-asala',
      languages: { ar: '/ar/shops/al-asala', en: '/en/shops/al-asala', 'x-default': '/ar/shops/al-asala' },
    });
    expect(localizedAlternates('/', 'ar').canonical).toBe('/ar');
  });
});

describe('sitemaps (R-WEB-10, Phase 17)', () => {
  it('the index lists one sitemap per locale, absolute', async () => {
    const { sitemapFiles, sitemapIndexXml } = await import('./sitemap');
    const index = sitemapIndexXml(sitemapFiles());
    expect(index).toContain('<sitemapindex');
    expect(index).toContain('/sitemaps/ar.xml</loc>');
    expect(index).toContain('/sitemaps/en.xml</loc>');
  });

  it('a locale sitemap lists that locale with every alternate and the Arabic x-default, escaped', async () => {
    const { localeSitemapXml } = await import('./sitemap');
    const xml = localeSitemapXml('en', [
      { path: '/' },
      { path: '/shops/a&b', lastModified: '2026-09-30T10:00:00Z' },
    ]);
    expect(xml).toContain('<loc>http://localhost:3000/en</loc>');
    expect(xml).toContain(
      '<loc>http://localhost:3000/en/shops/a&amp;b</loc><lastmod>2026-09-30T10:00:00Z</lastmod>',
    );
    expect(xml).toContain('hreflang="ar" href="http://localhost:3000/ar/shops/a&amp;b"');
    expect(xml).toContain('hreflang="x-default" href="http://localhost:3000/ar"');
    expect(xml).not.toContain('a&b');
  });
});
