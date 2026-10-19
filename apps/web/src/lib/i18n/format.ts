import type { Locale } from '@/i18n/routing';
import { OPERATING_CURRENCY, OPERATING_TIME_ZONE } from './config';

/**
 * Locale-aware formatting (spec §5/§6, decision D-040).
 *
 * Numeral rule from the design (design/analysis/01 §1.5):
 * - Clock times and date text: Arabic-Indic digits in Arabic, 12-hour clock ("٥:٣٠ م").
 * - Measurable quantities (price, distance, rating, counts, phone): Latin digits ("85 ر.س", "2.4 كم").
 * - English and Turkish use Latin digits everywhere; Turkish keeps its 24-hour clock ("17:30") and decimal comma.
 * The Gregorian calendar is always forced: plain `ar-SA` defaults to the Umm al-Qura (Hijri) calendar.
 */
export type AppLocale = Locale;

/** Clock times and date text. */
export const TIME_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-arab-ca-gregory',
  en: 'en-GB-u-nu-latn-ca-gregory',
  tr: 'tr-TR-u-nu-latn-ca-gregory',
};

/** Relative times ("قبل ٣ أيام" / "3 days ago" / "3 gün önce"), clock-style digits in Arabic. */
export const RELATIVE_TIME_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-arab',
  en: 'en-GB',
  tr: 'tr-TR',
};

const QUANTITY_LOCALE: Record<AppLocale, string> = {
  ar: 'ar-SA-u-nu-latn',
  en: 'en-US-u-nu-latn',
  tr: 'tr-TR-u-nu-latn',
};

const CURRENCY_LABEL: Record<AppLocale, Record<string, string>> = {
  ar: { SAR: 'ر.س' },
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

/** "٥:٣٠ م" (ar) · "5:30 pm" (en) · "17:30" (tr) */
export function formatTime(value: DateInput, locale: AppLocale, timeZone = OPERATING_TIME_ZONE): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], {
    ...CLOCK[locale],
    minute: '2-digit',
    timeZone,
  }).format(toDate(value));
}

/** "٥ م" (ar) · "5 pm" (en) · "17" (tr): the hour of a time, for the hour-then-minutes time picker. */
export function formatHour(value: DateInput, locale: AppLocale, timeZone = OPERATING_TIME_ZONE): string {
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], { ...CLOCK[locale], timeZone }).format(toDate(value));
}

/** "الجمعة ١٨ سبتمبر" (ar) · "Friday 18 September" (en); `withYear` appends the year. */
export function formatDate(
  value: DateInput,
  locale: AppLocale,
  options: { withYear?: boolean; withWeekday?: boolean; timeZone?: string } = {},
): string {
  const { withYear = false, withWeekday = true, timeZone = OPERATING_TIME_ZONE } = options;
  return new Intl.DateTimeFormat(TIME_LOCALE[locale], {
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

/** Day-of-month number for calendar cells — a quantity, so always Latin digits (design 3650–3668). */
export function formatDayNumber(value: DateInput, locale: AppLocale, timeZone = OPERATING_TIME_ZONE): string {
  return new Intl.DateTimeFormat(QUANTITY_LOCALE[locale], { day: 'numeric', timeZone }).format(toDate(value));
}

export function formatNumber(value: number, locale: AppLocale, maximumFractionDigits = 2): string {
  return new Intl.NumberFormat(QUANTITY_LOCALE[locale], {
    minimumFractionDigits: 0,
    maximumFractionDigits,
  }).format(value);
}

/** "85 ر.س" (ar) · "SAR 85" (en) · "85 SAR" (tr). Prices are informational only — there is no payment in v1. */
export function formatPrice(
  amount: number,
  locale: AppLocale,
  currency: string = OPERATING_CURRENCY,
): string {
  const number = formatNumber(amount, locale, 2);
  const label = CURRENCY_LABEL[locale][currency] ?? currency;
  return UNITS[locale].numberFirst ? `${number} ${label}` : `${label} ${number}`;
}

/** "2.4 كم" (ar) · "2.4 km" (en) · "2,4 km" (tr) */
export function formatDistanceKm(kilometres: number, locale: AppLocale): string {
  return `${formatNumber(kilometres, locale, 1)} ${UNITS[locale].km}`;
}

/** "4.8" — always one decimal, Latin digits. */
export function formatRating(value: number, locale: AppLocale): string {
  return new Intl.NumberFormat(QUANTITY_LOCALE[locale], {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(value);
}

/** Service duration follows the clock-time digit rule: "٣٠ دقيقة" (ar) · "30 min" (en) · "30 dk" (tr). */
export function formatDurationMinutes(minutes: number, locale: AppLocale): string {
  return `${new Intl.NumberFormat(TIME_LOCALE[locale]).format(minutes)} ${UNITS[locale].minutes}`;
}

/**
 * Display grouping for an E.164 Saudi mobile: "+966 51 234 5678". Other numbers are returned unchanged.
 * Callers must render the result inside an LTR-isolated element (see <Ltr>).
 */
export function formatPhone(e164: string): string {
  const match = /^\+966(5\d)(\d{3})(\d{4})$/.exec(e164);
  return match ? `+966 ${match[1]} ${match[2]} ${match[3]}` : e164;
}
