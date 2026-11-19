import { type AppLocale, formatDate, formatDurationLong, formatPrice, formatTime } from '@/lib/i18n/format';

/** The message key under `adminAudit.summaries` for a summary code (next-intl nests on dots). */
export function summaryKey(code: string): string {
  return code.replace(/\./g, '_');
}

/** Amounts in the record's currency. */
const MONEY = new Set([
  'amount',
  'amountFrom',
  'amountTo',
  'price',
  'priceFrom',
  'priceTo',
  'standardAmount',
  'rewardAmount',
]);

/** Lengths in minutes, written in words («٥ دقائق», «ساعتان»). */
const MINUTES = new Set(['duration', 'durationFrom', 'durationTo', 'slotStep', 'minLeadTime', 'buffer']);

/** Arguments the messages choose on (ICU `select`): passed as recorded, never worded. */
const RAW = new Set([
  'by',
  'change',
  'number',
  'enabled',
  'numberChanged',
  'demo',
  'kind',
  'automatic',
  'custom',
  'image',
  'rewardType',
  'inactivityRule',
  'notifications',
  'whatsApp',
  'online',
]);

/** Plain counts, for plural rules and the reader's digits. */
const COUNTS = new Set([
  'count',
  'attempt',
  'version',
  'priceVersion',
  'firstVisit',
  'visit',
  'pointsForReward',
  'inactivityDays',
  'horizonDays',
  'length',
  'rating',
]);

/**
 * Every optional argument a message may name; one the record left out reads `none`, which the messages turn into
 * nothing (ICU `select … none {}`).
 */
const OPTIONAL = [
  'from',
  'to',
  'price',
  'currency',
  'priceFrom',
  'priceTo',
  'durationFrom',
  'durationTo',
  'name',
  'category',
  'online',
  'barbers',
  'resumesAt',
  'automatic',
  'priceVersion',
  'custom',
  'standardAmount',
  'amountFrom',
  'amountTo',
  'endFrom',
  'endTo',
  'rewardAmount',
  'inactivityDays',
];

const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/;
const DAY = /^\d{4}-\d{2}-\d{2}$/;

/**
 * The values a summary's message uses (D-155, the client's 8-3-6: the activity log fully in the reader's language):
 * dates and times on the reader's calendar, amounts with their currency, lengths in words, counts as numbers, the Arabic
 * or English name by the reader's language, and each recorded word (a status, a field, an event…) through `word`, lists
 * joined the reader's way. Anything the dictionary does not know is shown as recorded.
 */
export function summaryValues(
  args: Readonly<Record<string, string>>,
  locale: AppLocale,
  word: (value: string) => string,
): Record<string, string | number> {
  const list = (value: string) =>
    value === ''
      ? word('nothing')
      : new Intl.ListFormat(locale, { type: 'conjunction' }).format(
          value.split(',').map((item) => word(item)),
        );
  const values: Record<string, string | number> = {};
  for (const name of OPTIONAL) values[name] = 'none';
  for (const [name, value] of Object.entries(args)) {
    if (RAW.has(name)) values[name] = value;
    else if (MONEY.has(name)) values[name] = formatPrice(Number(value), locale, args.currency ?? 'SAR');
    else if (MINUTES.has(name)) values[name] = formatDurationLong(Number(value), locale);
    else if (COUNTS.has(name) && /^\d+$/.test(value)) values[name] = Number(value);
    else if (INSTANT.test(value)) values[name] = `${formatDate(value, locale)} ${formatTime(value, locale)}`;
    else if (DAY.test(value))
      values[name] = formatDate(`${value}T12:00:00Z`, locale, {
        withYear: true,
        withWeekday: false,
        timeZone: 'UTC',
      });
    else if (name === 'changes') values[name] = shopMessageChanges(value, locale, word);
    else values[name] = list(value);
  }
  // The name in the reader's language.
  if (args.planAr !== undefined) values.plan = locale === 'ar' ? args.planAr : (args.planEn ?? args.planAr);
  if (args.nameAr !== undefined) values.name = locale === 'ar' ? args.nameAr : (args.nameEn ?? args.nameAr);
  return values;
}

/** `whatsapp_shop_messages.saved`: «Customer.BookingConfirmed off» items as «العميل: تأكيد الحجز — موقوفة». */
function shopMessageChanges(value: string, locale: AppLocale, word: (value: string) => string): string {
  const items = value
    .split(',')
    .filter(Boolean)
    .map((item) => {
      const [slot, state] = item.trim().split(' ') as [string, string | undefined];
      const [audience, event] = slot.split('.') as [string, string | undefined];
      return `${word(audience)}: ${word(event ?? '')} — ${word(state ?? '')}`;
    });
  return items.length === 0
    ? word('nothing')
    : new Intl.ListFormat(locale, { type: 'conjunction' }).format(items);
}
