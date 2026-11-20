import type { Locale } from '@/i18n/routing';
import type { DisplayCalendar } from './calendar';
import { OPERATING_CURRENCY, OPERATING_TIME_ZONE } from './config';

/**
 * Locale-aware formatting (spec §5/§6, decision D-040).
 *
 * Numeral rule (D-148, the client's review; it replaces design/analysis/01 §1.5's Latin quantities):
 * - Arabic: Arabic-Indic digits everywhere — clock times, dates, prices, durations, distances, ratings, counts
 *   ("٥:٣٠ م", "٨٥ ⃁", "٢٫٤ كم"), with the new Saudi Riyal sign (U+20C1, drawn by the bundled one-glyph font).
 *   Phone numbers stay as typed (Latin, left to right).
 * - English and Turkish use Latin digits everywhere and «SAR»; Turkish keeps its 24-hour clock ("17:30") and decimal comma.
 * The Gregorian calendar is forced by default: plain `ar-SA` defaults to the Umm al-Qura (Hijri) calendar. A shop user
 * may read date text in Hijri instead (D-151, `calendar`); clock times never change.
 */
export type AppLocale = Locale;

/** Clock times and date text. */
export const TIME_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-arab-ca-gregory',
  en: 'en-GB-u-nu-latn-ca-gregory',
  tr: 'tr-TR-u-nu-latn-ca-gregory',
};

/** The date-text locale in the chosen calendar (D-151): Gregorian unless a shop user picked Hijri. */
export function dateLocale(locale: AppLocale, calendar: DisplayCalendar = 'gregory'): string {
  return calendar === 'gregory'
    ? TIME_LOCALE[locale]
    : TIME_LOCALE[locale].replace('ca-gregory', `ca-${calendar}`);
}

/** Relative times ("قبل ٣ أيام" / "3 days ago" / "3 gün önce"), clock-style digits in Arabic. */
export const RELATIVE_TIME_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-arab',
  en: 'en-GB',
  tr: 'tr-TR',
};

const QUANTITY_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-arab',
  en: 'en-US-u-nu-latn',
  tr: 'tr-TR-u-nu-latn',
};

/** The Riyal sign U+20C1 in Arabic (D-148): it follows the number, so it reads to its left. */
export const RIYAL_SIGN = '\u20C1';

const CURRENCY_LABEL: Record<AppLocale, Record<string, string>> = {
  ar: { SAR: RIYAL_SIGN },
  en: { SAR: 'SAR' },
  tr: { SAR: 'SAR' },
};

/** The hour part of a clock time: 12-hour in Arabic and English (the design), 24-hour in Turkish. */
const CLOCK: Record<AppLocale, Pick<Intl.DateTimeFormatOptions, 'hour' | 'hour12' | 'hourCycle'>> = {
  ar: { hour: 'numeric', hour12: true },
  en: { hour: 'numeric', hour12: true },
  tr: { hour: '2-digit', hourCycle: 'h23' },
};

/** Word order and unit of the small quantities: Arabic and Turkish put the number first ("85 ر.س", "85 SAR"). */
const UNITS: Record<AppLocale, { numberFirst: boolean; km: string; minutes: string }> = {
  ar: { numberFirst: true, km: 'كم', minutes: 'دقيقة' },
  en: { numberFirst: false, km: 'km', minutes: 'min' },
  tr: { numberFirst: true, km: 'km', minutes: 'dk' },
};

type DateInput = Date | string | number;

const toDate = (value: DateInput): Date => (value instanceof Date ? value : new Date(value));

/** "٥:٣٠ م" (ar) — "5:30 pm" (en) — "17:30" (tr) */
export function formatTime(value: DateInput, locale: AppLocale, timeZone = OPERATING_TIME_ZONE): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], {
    ...CLOCK[locale],
    minute: '2-digit',
    timeZone,
  }).format(toDate(value));
}

/** "٥ م" (ar) — "5 pm" (en) — "17" (tr): the hour of a time, for the hour-then-minutes time picker. */
export function formatHour(value: DateInput, locale: AppLocale, timeZone = OPERATING_TIME_ZONE): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], { ...CLOCK[locale], timeZone }).format(toDate(value));
}

/** "الجمعة ١٨ سبتمبر" (ar) — "Friday 18 September" (en); `withYear` appends the year. */
export function formatDate(
  value: DateInput,
  locale: AppLocale,
  options: { withYear?: boolean; withWeekday?: boolean; timeZone?: string; calendar?: DisplayCalendar } = {},
): string {
  const { withYear = false, withWeekday = true, timeZone = OPERATING_TIME_ZONE, calendar } = options;
  return new Intl.DateTimeFormat(dateLocale(locale, calendar), {
    weekday: withWeekday ? 'long' : undefined,
    day: 'numeric',
    month: 'long',
    year: withYear ? 'numeric' : undefined,
    timeZone,
  }).format(toDate(value));
}

/** Month name for a date block ("سبتمبر" / "Sep"). */
export function formatMonthShort(
  value: DateInput,
  locale: AppLocale,
  timeZone = OPERATING_TIME_ZONE,
): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], {
    month: locale === 'ar' ? 'long' : 'short',
    timeZone,
  }).format(toDate(value));
}

/** A calendar month, from its first day ("2026-11-01"): "نوفمبر ٢٠٢٦" (ar) — "November 2026" (en). */
export function formatMonthYear(firstDay: string, locale: AppLocale): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${firstDay.slice(0, 10)}T12:00:00Z`));
}

/**
 * Day-of-month number for calendar cells and the booking day strip (design 3650–3668), in the D-148 digits.
 * Always Gregorian (D-156): Safari reads plain `ar-SA` as the Umm al-Qura calendar, so on an iPhone the strip showed
 * the Hijri day under a Gregorian month («سبت ١٨ نوفمبر» for Saturday 28 November). `TIME_LOCALE` forces `ca-gregory`.
 */
export function formatDayNumber(value: DateInput, locale: AppLocale, timeZone = OPERATING_TIME_ZONE): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], { day: 'numeric', timeZone }).format(toDate(value));
}

export function formatNumber(value: number, locale: AppLocale, maximumFractionDigits = 2): string {
  return new Intl.NumberFormat(QUANTITY_LOCALE[locale], {
    minimumFractionDigits: 0,
    maximumFractionDigits,
  }).format(value);
}

/** "٨٥ ⃁" (ar) — "SAR 85" (en) — "85 SAR" (tr). Prices are informational only — there is no payment in v1. */
export function formatPrice(
  amount: number,
  locale: AppLocale,
  currency: string = OPERATING_CURRENCY,
): string {
  const number = formatNumber(amount, locale, 2);
  const label = CURRENCY_LABEL[locale][currency] ?? currency;
  return UNITS[locale].numberFirst ? `${number} ${label}` : `${label} ${number}`;
}

/** "2.4 كم" (ar) — "2.4 km" (en) — "2,4 km" (tr) */
export function formatDistanceKm(kilometres: number, locale: AppLocale): string {
  return `${formatNumber(kilometres, locale, 1)} ${UNITS[locale].km}`;
}

/** "٤٫٨" (ar) — "4.8" (en) — always one decimal. */
export function formatRating(value: number, locale: AppLocale): string {
  return new Intl.NumberFormat(QUANTITY_LOCALE[locale], {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(value);
}

/**
 * Arabic counted nouns (D-155, the client's 2-2): «دقيقة واحدة», «دقيقتان», «٥ دقائق» (3–10), «١٥ دقيقة» (11 and up).
 * The singular and dual carry no number, as written Arabic does.
 */
const ARABIC_COUNTED = {
  minutes: { one: 'دقيقة واحدة', two: 'دقيقتان', few: 'دقائق', other: 'دقيقة' },
  hours: { one: 'ساعة واحدة', two: 'ساعتان', few: 'ساعات', other: 'ساعة' },
} as const;
const arabicPlural = new Intl.PluralRules('ar');

function arabicCount(value: number, unit: keyof typeof ARABIC_COUNTED): string {
  const words = ARABIC_COUNTED[unit];
  const rule = arabicPlural.select(value);
  if (rule === 'one') return words.one;
  if (rule === 'two') return words.two;
  return `${new Intl.NumberFormat(TIME_LOCALE.ar).format(value)} ${rule === 'few' ? words.few : words.other}`;
}

/** Service duration follows the clock-time digit rule: "٣٠ دقيقة", "٥ دقائق" (ar) — "30 min" (en) — "30 dk" (tr). */
export function formatDurationMinutes(minutes: number, locale: AppLocale): string {
  if (locale === 'ar') return arabicCount(minutes, 'minutes');
  return `${new Intl.NumberFormat(TIME_LOCALE[locale]).format(minutes)} ${UNITS[locale].minutes}`;
}

/**
 * A setting's length (D-155, the client's 2-2): whole hours as hours, "٦ ساعات" rather than "٣٦٠ دقيقة" (ar) — "6 hours"
 * (en) — "6 saat" (tr); anything else in minutes.
 */
export function formatDurationLong(minutes: number, locale: AppLocale): string {
  if (minutes < 60 || minutes % 60 !== 0) return formatDurationMinutes(minutes, locale);
  const hours = minutes / 60;
  if (locale === 'ar') return arabicCount(hours, 'hours');
  const number = new Intl.NumberFormat(TIME_LOCALE[locale]).format(hours);
  return locale === 'en' ? `${number} ${hours === 1 ? 'hour' : 'hours'}` : `${number} saat`;
}

/**
 * Display grouping for an E.164 Saudi mobile: "+966 51 234 5678". Other numbers are returned unchanged.
 * Callers must render the result inside an LTR-isolated element (see <Ltr>).
 */
export function formatPhone(e164: string): string {
  const match = /^\+966(5\d)(\d{3})(\d{4})$/.exec(e164);
  return match ? `+966 ${match[1]} ${match[2]} ${match[3]}` : e164;
}
