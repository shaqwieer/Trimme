/**
 * Structured data (spec §6, R-WEB-11). Values come from stored data only: an AggregateRating is emitted only when the
 * shop or professional has at least one published review.
 */

type Json = Record<string, unknown>;

/** Characters escaped in inline JSON-LD: < > & and the two JavaScript line terminators U+2028 and U+2029. */
const UNSAFE_CODES = new Set([0x3c, 0x3e, 0x26, 0x2028, 0x2029]);
const BACKSLASH = String.fromCharCode(92);

/**
 * Serializes structured data for an inline `<script type="application/ld+json">`. Shop-written text can contain `<`,
 * so every character that could close the script or change parsing is escaped.
 */
export function serializeJsonLd(data: Json | Json[]): string {
  let out = '';
  for (const character of JSON.stringify(data)) {
    const code = character.charCodeAt(0);
    out += UNSAFE_CODES.has(code) ? `${BACKSLASH}u${code.toString(16).padStart(4, '0')}` : character;
  }
  return out;
}

export function organizationLd(url: string, logoUrl: string, name: string): Json {
  return { '@context': 'https://schema.org', '@type': 'Organization', name, url, logo: logoUrl };
}

export function websiteLd(url: string, name: string, searchUrlTemplate: string, inLanguage: string): Json {
  return {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name,
    url,
    inLanguage,
    potentialAction: {
      '@type': 'SearchAction',
      target: { '@type': 'EntryPoint', urlTemplate: searchUrlTemplate },
      'query-input': 'required name=search_term_string',
    },
  };
}

export function breadcrumbLd(items: Array<{ name: string; url: string }>): Json {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: items.map((item, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: item.name,
      item: item.url,
    })),
  };
}

export function aggregateRatingLd(average: number, count: number): Json | undefined {
  return count > 0
    ? { '@type': 'AggregateRating', ratingValue: average, reviewCount: count, bestRating: 5, worstRating: 1 }
    : undefined;
}

const SCHEMA_DAY = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;
const DAY_INDEX: Record<string, number> = Object.fromEntries(SCHEMA_DAY.map((day, index) => [day, index]));

const clock = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;

/**
 * schema.org opening hours from the weekly windows (minutes from the weekday's midnight). A window that passes midnight
 * is split into its two calendar days, because `closes` must be a time of the same day.
 */
export function openingHoursLd(
  intervals: Array<{ day: string; startMinute: number; endMinute: number }>,
): Json[] {
  const specs: Json[] = [];
  for (const interval of intervals) {
    const day = DAY_INDEX[interval.day];
    if (day === undefined) continue;
    const firstEnd = Math.min(interval.endMinute, 24 * 60);
    specs.push({
      '@type': 'OpeningHoursSpecification',
      dayOfWeek: `https://schema.org/${SCHEMA_DAY[day]}`,
      opens: clock(interval.startMinute),
      closes: firstEnd === 24 * 60 ? '23:59' : clock(firstEnd),
    });
    if (interval.endMinute > 24 * 60) {
      specs.push({
        '@type': 'OpeningHoursSpecification',
        dayOfWeek: `https://schema.org/${SCHEMA_DAY[(day + 1) % 7]}`,
        opens: '00:00',
        closes: clock(interval.endMinute - 24 * 60),
      });
    }
  }
  return specs;
}
