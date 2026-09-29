import { type AppLocale, formatTime } from '@/lib/i18n/format';

/** The opening status the API computes (the engine's rules, D-091); the web only words it. */
export type OpenStatus = {
  isOpenNow: boolean;
  closesAt?: string | null;
  nextOpensAt?: string | null;
};

/** The `opening` messages translator (next-intl's `useTranslations('opening')` or `getTranslations`). */
export type OpeningMessages = (
  key: 'openUntil' | 'opensAt' | 'opensOn' | 'closed',
  values?: Record<string, string>,
) => string;

function localDay(instant: string | Date, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date(instant));
}

/**
 * "مفتوح حتى ١١:٠٠ م" / "يفتح ٢:٠٠ م" / "يفتح الأحد ٩:٠٠ ص" / "مغلق" (design shop cards and the shop page). The weekday is
 * added only when the next opening is not today in the shop's time zone.
 */
export function openingLabel(
  t: OpeningMessages,
  locale: AppLocale,
  status: OpenStatus,
  timeZone: string,
  now: Date = new Date(),
): string {
  if (status.isOpenNow && status.closesAt) {
    return t('openUntil', { time: formatTime(status.closesAt, locale, timeZone) });
  }
  if (status.nextOpensAt) {
    const time = formatTime(status.nextOpensAt, locale, timeZone);
    if (localDay(status.nextOpensAt, timeZone) === localDay(now, timeZone)) {
      return t('opensAt', { time });
    }
    const day = new Intl.DateTimeFormat(locale === 'ar' ? 'ar-SA-u-ca-gregory' : 'en-GB', {
      weekday: 'long',
      timeZone,
    }).format(new Date(status.nextOpensAt));
    return t('opensOn', { day, time });
  }
  return t('closed');
}

/** "اليوم" / "غداً" prefix for the earliest-slot chip: true when the instant is tomorrow in the shop's time zone. */
export function isTomorrow(instant: string, timeZone: string, now: Date = new Date()): boolean {
  const tomorrow = new Date(now.getTime() + 24 * 60 * 60 * 1000);
  return localDay(instant, timeZone) === localDay(tomorrow, timeZone);
}
